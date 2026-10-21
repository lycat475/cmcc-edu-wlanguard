# 技术设计

记录这套东西是**怎么被逆向出来的**，以及为什么代码长这样。如果你只是想用，看 README 就够了。

> **版本说明**
> 本文档分两部分：
> - **2.x（C# 重写，当前）** — 见第 0 章
> - **1.x（PowerShell，已过期）** — 见文末附录，仅作历史参考
>
> Portal 协议部分（认证链路、cookie 免密、下线）两个版本完全一致，附录里那些抓包结论仍然有效。

---

# 第 0 章　2.x：C# 重写

## 0.1 为什么重写

1.0.x 是 PowerShell + WinForms，能跑但有三个硬伤：

| 问题 | 具体表现 |
| --- | --- |
| **依赖链脆弱** | `.vbs` 拉起 PowerShell → 加载脚本 → 建窗口；杀软对「脚本宿主 + 隐藏窗口 + ExecutionPolicy Bypass」很敏感，火绒实际拦过 |
| **语言怪癖导致 bug** | `$Tray` 与 `$script:tray` 同名互相覆盖（托盘图标几个月没创建成功）；4 个定时器共用 `$t` 导致弹窗死循环 |
| **异常被吞** | 105 处空 `catch {}`，上面那个托盘 bug 就是被它们隐藏的 |

重写目标是**行为不变、依赖归零、错误可见**。

## 0.2 模块划分

```
src/
├─ Program.cs        入口：命令行分发、单实例互斥 + 唤起事件
├─ AppEnv.cs         全局路径与启动参数（内嵌在 Program.cs）
├─ AppLog.cs         日志：统一格式，写失败降级到错误日志
├─ Config.cs         配置：字典原样回写 + DPAPI + 文本缓存
├─ Status.cs         运行时状态文件（原子写）
├─ Network.cs        在线检测、SSID、本机 IP
├─ Portal.cs         Portal 协议：认证 / cookie 免密 / 验证码 / 短信 / 下线
├─ LoginRunner.cs    登录编排：什么时候用 cookie、什么时候必须人工
├─ Guard.cs          守护循环：退避、网络事件唤醒、进程管理
├─ Autostart.cs      计划任务（开机自启）
├─ IconFactory.cs    图标合成（带缓存，防 GDI 句柄泄漏）
├─ Dialogs.cs        验证码输入框 + UI 线程回调适配
└─ MainForm.cs       主界面
```

分层的原则：**协议（Portal）不知道 UI 存在，UI 不碰 HTTP**。
两者之间用 `ILoginPrompter` 接口连接——守护传 `null`（永不弹窗），面板传 `FormPrompter`。

## 0.3 登录流程图

```
                    ┌─────────────┐
                    │ IsOnline()  │  generate_204 必须 204
                    └──────┬──────┘  或 connecttest 内容匹配
                     在线  │  离线
                    ┌──────┴──────┐
                    ▼             ▼
              写状态 online   ┌──────────────┐
                             │ 配置是否完整 │
                             └──┬────────┬──┘
                          不完整 │        │ 完整
                                ▼        ▼
                       写 offline+提示  ┌──────────────┐
                                      │ 在目标 SSID？│
                                      └──┬────────┬──┘
                                     否  │        │ 是
                                        ▼        ▼
                                    跳过      LoginRunner.AutoLogin
                                                │
                        ┌───────────────────────┼────────────────────┐
                        ▼                       ▼                    ▼
                 有 cookie → GET           cookie 失效          动态模式无 cookie
                 成功 → online           ┌────┴────┐                │
                                        ▼         ▼                ▼
                                  动态 → 人工   静态 → 密码     提示人工
```

三种登录方式的区别：

| 方式 | 入口 | 是否发短信 | 能否自动 |
| --- | --- | --- | --- |
| cookie 免密 | 守护循环 / `cookies登录` 按钮 | 否 | ✅ |
| 静态密码 | 守护循环 / `静态密码登录` 按钮 | 否 | ✅ |
| 动态密码（短信） | 仅 `发送验证码并登录` 按钮 | 是 | ❌ 必须人工 |

**代码级保证**：`Portal.Login` 在动态模式下，如果 `prompter == null`（即守护流程），直接返回 `need-manual`，永不发短信。

## 0.4 守护循环

```csharp
while (true) {
    读配置（带缓存）→ 判在线 → 判配置 → 判 SSID → 判是否需要认证
    按结果写状态 → 计算下次间隔 → 可被网络事件唤醒的睡眠
}
```

退避规则：

| 情况 | 间隔 |
| --- | --- |
| 在线 | 配置值（默认 5~60 秒） |
| 等待人工 | 配置值（用户操作后尽快生效） |
| 连续失败第 n 次 | `interval × 2^n`，上限 30 分钟 |

网络事件唤醒：订阅 `NetworkAvailabilityChanged` / `NetworkAddressChanged`，
睡眠切成 500ms 片，事件触发时立刻醒——不用干等一个完整间隔。

## 0.4.1 免密 cookie 的续期（2.0.1 起自动）

**实测结论**：服务端下发的 4 个 `AUTOAUTH_*` cookie，有效期是「**登录成功那一刻 + 90 天**」。

| 登录时间 | 服务端给的到期日 |
| --- | --- |
| 2026-10-20 22:25:57 | 2027-01-18 22:25:57 |
| 2026-10-21 12:15:29 | 2027-01-19 12:15:29 |
| 2026-10-21 12:21:03 | 2027-01-19 12:21:03 |

两行都是精确的 +90 天，说明是**滚动有效期**，不是固定到期日。

> 踩过的坑：单日之内连测四次，到期日都是同一天，看起来像「固定到期日」。
> 跨一天再测才看出差 1 天——**同日样本无法区分「固定」和「滚动」**。

**实现**：

```
完整登录成功（短信/密码 POST）
  → 从 CookieContainer 里筛出 4 个 AUTOAUTH_*
  → 取最大 Expires 作为到期时间
  → LoginResult.AutoCookies / AutoCookiesExpire
  → LoginRunner.SaveRefreshedCookies()
      → 与配置里的旧值比对，相同则跳过
      → SetSecret("autoCookies", "autoCookiesEnc", ...)
      → SetString("autoCookiesExpire", ...)
      → Save()
```

cookie 免密登录（GET `authServlet`）不会产生新的 Set-Cookie，所以**不续期**——只有完整登录才会。

## 0.5 与 1.x 的关键差异

| 维度 | 1.x（PowerShell） | 2.x（C#） |
| --- | --- | --- |
| 进程模型 | 面板 spawn 子进程跑认证 | 面板后台线程跑认证，验证码直接弹窗 |
| 验证码传递 | `captcha.req.json` 文件轮询 | `ILoginPrompter` 进程内回调 |
| 单实例 | 第二个实例静默退出 | 命名事件唤起已有窗口 |
| 异常 | 105 处空 catch | 每处 catch 写明处理方式 |
| 图标 | 运行时合成，ICO 自带帧是坏的 | 7 尺寸重渲染 + 句柄缓存 |
| 配置读取 | 每次重读重解析 | 按文件时间戳缓存原文 |

## 0.6 配置格式（与 1.x 兼容）

```json
{
  "portal": "",
  "acName": "",
  "ssid": "CMCC-EDU",
  "loginMode": "dynamic",
  "interval": 60,
  "usernameEnc": "dpapi:...",
  "passwordEnc": "dpapi:...",
  "autoCookiesEnc": "dpapi:...",
  "autoCookiesExpire": "2027-01-19 12:21:03"
}
```

- 用「字典读入 → 原样回写」，不认识的键不会被丢弃
- `autoCookiesExpire` 是明文（只是到期时间，不是凭据），登录成功后自动写入，面板据此显示剩余天数
- `portal` / `acName` **默认为空**：未认证时 AC 的 302 地址里天然带 `wlanuserip` 和 `wlanacname`，程序据此自动识别并写进日志；只有识别失败才需要手填
- 凭据一律 `dpapi:` 前缀（Windows DPAPI，绑定当前用户）
- 首次运行没有 `config.json` 时自动生成默认配置

## 0.7 构建与打包

```
build.ps1        csc.exe（.NET Framework 4.8）编译 src/*.cs → dist/*.exe
package.ps1      发版打包（维护者本地工具，不随仓库发布）
```

不用 dotnet SDK：**csc.exe 系统自带，产物是单文件 exe，目标机不需要装任何运行时**。

---

# 附录：1.x 版本设计（已过期）

> 以下内容是 PowerShell 版（1.0.x）的原始设计文档，**保留原样**。
> 注意：1.x 的代码文件（`.ps1` / `.vbs`）已从仓库移除，本文仅作设计记录。
>
> 其中 **第 1、2、3 章（Portal 协议）的结论对 2.x 依然有效**，因为协议没变；
> 第 4、5 章描述的进程模型和文件 IPC 已被 C# 版取代，仅供参考。

---
## 1.x 原文

记录这套东西是**怎么被逆向出来的**，以及为什么代码长这样。如果你只是想用，看 README 就够了。

---

## 1. Portal 认证流程

### 1.1 完整链路

```
① 随便访问一个 http 地址（默认 http://www.baidu.com/）
        ↓ 未认证时网关（AC）拦下并 302
② 得到 frameset 页，里面有两个 frame：
      hiddenFrame → blank.html
      mainFrame   → /style/university/index.jsp?paramStr=...
        ↓
③ 加载 index.jsp，建立 JSESSIONID / X-LB 会话，并从 HTML 里取出动态的 paramStr
        ↓
④ POST /authServlet，表单含 paramStr / UserType / pwdType / UserName / PassWord / autoload
        ↓
⑤ 落点 logon.jsp = 成功，login_fail.jsp = 失败
        ↓
⑥ 用 generate_204 探测确认外网真的通了
```

### 1.2 一个容易踩的坑

直接打开 `http://<Portal地址>/?wlanuserip=...&wlanacname=...&ssid=CMCC-EDU` **不能认证**。

这个地址只是外层 frameset，真正的登录页在内层 frame 里。所以程序必须**先触发 AC 的重定向**，或者手动构造带 `wlanuserip` / `wlanacname` 的地址让服务端返回 frameset，然后跟进 `mainFrame`。

### 1.3 关键字段

| 字段 | 值 | 说明 |
| --- | --- | --- |
| `paramStr` | 每次动态生成 | 从 index.jsp 的隐藏 input 里取，约 400~440 字符 |
| `UserType` | `1` | 固定 |
| `pwdType` | `1` 静态密码 / `2` 动态短信 | |
| `autoload` | `checked` / `test` | 对应「自动登录」勾选框 |

---

## 2. Cookie 免密登录（核心发现）

### 2.1 凭据形态

在认证页勾选「自动登录」并成功登录一次后，服务端下发 **4 个持久 cookie**：

```
AUTOAUTH_USERNAME_UNIVERSITY    = <已脱敏>
AUTOAUTH_VALPERIOD_UNIVERSITY   = <已脱敏>
AUTOAUTH_BOOKTIME_UNIVERSITY    = <已脱敏>
AUTOAUTH_APPLYID_UNIVERSITY     = <已脱敏>
```

> 实际值是 Base64 字符串，长度约 20～70 字符，其中 `APPLYID` 最长、带签名。
> 每个账号、每次开通都不一样，本文不保留真实值。

`UNIVERSITY` 后缀对应 Portal 的 `style/university/` 皮肤。值都是 Base64 的加密/签名串，不可逆推。浏览器里看到的 `Expires` 约 3 个月后（文档声称「到当月月底」，实测比这长）。

### 2.2 触发方式（重点）

带上这 4 个 cookie 访问认证页，**服务端返回的 frameset 会变**：

| 有无 cookie | mainFrame 指向 |
| --- | --- |
| 无 | `/style/university/index.jsp?paramStr=...`（正常登录页） |
| 有 | **`/authServlet?paramStr=...`**（直接认证） |

所以免密登录只需要**一个 GET**：

```powershell
# 1) 建会话并注入 4 个 cookie
# 2) GET /?wlanuserip=...&wlanacname=...&ssid=CMCC-EDU
# 3) 从 frameset 取 mainFrame
# 4) 如果 mainFrame 是 authServlet → GET 它 → 落点 logon.jsp = 成功
```

### 2.3 走错的路

一开始照抄某个 OpenWrt 项目的做法，往 `/authServlet` POST：

```
isCookie=true & cookieType=2 & cookie=2 & UserType=1 & UserName='' & PassWord=''
```

结果**服务端 500**。那套参数是别的 Portal 版本的协议，这个版本根本不认。

正确做法是**不要 POST**——直接 GET `authServlet`，让服务端自己从 cookie 里认人。这个区别是靠观察「带 cookie 后 frameset 变了」才发现的。

### 2.4 怎么判断成功

落点 URL 里含 `logon.jsp` 就是成功，含 `login_fail.jsp` 就是失败。这个判断比解析页面文字可靠得多。

---

## 3. 下线

```powershell
GET /logoutServlet?paramStr=<logon.jsp 里的 paramStr>
```

**必须带 `paramStr`**，不带会返回「系统异常,下线失败」并跳到 `default/logout.jsp`（注意是 default 不是 university，这个路径本身就是一个失败信号）。

验证方式：调用后 3 秒探测 `generate_204`，从 204 变成不通就是真的下线了。

---

## 4. 守护进程状态机

```
        ┌──────────── 每个周期 ────────────┐
        ↓                                 │
   探测外网（generate_204）               │
        │                                 │
   ┌────┴────┐                            │
  在线      断网                           │
   │         │                            │
   │    ┌────┴─────┐                      │
   │  有cookie  无cookie                  │
   │    │         │                      │
   │  cookie     动态模式？               │
   │  登录         │                       │
   │    │      是 → 提示人工              │
   │    │      否 → 密码登录              │
   │    └────┬─────┘                      │
   └─────────┴────────────────────────────┘
```

### 4.1 状态

| 状态 | 含义 | 面板显示 |
| --- | --- | --- |
| `online` | 已联网 | 绿色圆点 |
| `offline` | 未联网 | 红色圆点 |
| `busy` | 另一次认证进行中 | 黄色 |
| `captcha` | 等待人工输入验证码 | 黄色 |
| `error` | 配置或运行异常 | 红色 |

状态写到 `status.json`，面板每 2 秒读一次。写入用「临时文件 + 原子替换」，避免面板读到半截 JSON。

### 4.2 为什么还要轮询

认证会话过期属于 **L3 断网**——WiFi 链路一直连着，Windows 不会产生任何网络事件。

所以纯事件驱动会漏掉最常见的那种断网。现在的组合是：

- **网络事件唤醒**：订阅 `Win32_NetworkAdapter` 的 `NetConnectionStatus=2`，WiFi 一连上就打断睡眠立刻探测
- **轮询兜底**：默认 60 秒一次，断网时缩短到 15 秒快速重试

### 4.3 退避

| 情况 | 间隔 |
| --- | --- |
| 正常 | `interval`（默认 60 秒） |
| 失败第 n 次 | `interval × 2^n`，上限 10 分钟 |
| 失败 ≥2 次且原因是密码/账号类 | 30 分钟 |

最后一条是为了别把账号打到触发图形验证码。

### 4.4 动态模式为什么不自动登录

动态密码每次认证都会**发一条短信**。如果守护每 30 秒自动试一次，用户会收到短信轰炸。

所以规则是：

- 有 cookie → 自动认证（不花短信，也不用人）
- 无 cookie 或 cookie 失效 + 动态模式 → 只提示，等用户点按钮
- 只有静态密码模式才会无人值守地反复重试

---

## 5. 界面与进程协作

### 5.1 进程模型

```
wscript.exe  run-hidden.vbs  -Loop
   └─ powershell.exe  cmcc-edu-autologin.ps1 -Loop      ← 守护，无窗口

wscript.exe  打开守护面板.vbs  [-Tray]
   └─ powershell.exe  cmcc-edu-gui.ps1 [-Tray]          ← 面板，WinForms
```

用 `.vbs` 启动而不是直接双击 `.ps1`，是因为本机执行策略是 `Restricted`，VBS 能用 `-ExecutionPolicy Bypass` 绕过，而且不弹黑框。

### 5.2 互斥量

| 名称 | 作用 |
| --- | --- |
| `Local\CmccEduGuardLoop` | 保证只有一个守护 |
| `Local\CmccEduGuardLogin` | 认证临界区，守护和一次性登录互斥 |
| `Local\CmccEduGui` | 面板单实例 |

### 5.3 人工输入怎么传递

守护和面板是两个进程，靠**文件**通信：

```
守护：写 captcha.req.json { id, kind: 'captcha'|'code', at, hint }
      ↓
面板：每 2 秒检查这个文件，有新 id 就弹窗
      ↓
面板：用户输入后写 captcha.ans.json { id, code, retry, cancel }
      ↓
守护：轮询答案文件，id 匹配才采用
```

用 `id` 防止旧答案被误用，用 `at` 时间戳做 3 分钟过期判断。

---

## 6. 几个设计取舍

**为什么用 PowerShell 而不是 exe**
不需要编译、不需要安装、可以直接看源码。代价是有杀软误报风险。

**为什么配置曾经是明文 JSON**
简单、可手工编辑，但密码明文落盘是硬伤。**v1.0.0 起已改为 DPAPI 加密**，见第 7 节。

**为什么守护不主动心跳**
主动制造流量保活可能违反使用条款，也可能干扰用户的正常网络。只在检测到断网时才动作。

**为什么 cookie 优先而不是只用 cookie**
cookie 会失效（换终端、服务端清理、用户点了取消）。保留密码/短信路径作为兜底。

---

## 7. 凭据加密（v1.0.0 起）

### 7.1 为什么用 DPAPI

`config.json` 里原本是明文手机号、密码、`AUTOAUTH_*` cookie。一旦这个文件被拷走、传上网盘、误提交 Git，账号就直接送人。

方案对比：

| 方案 | 跨机器 | 要额外密码 | 适用性 |
| --- | --- | --- | --- |
| 明文 | 是 | 否 | 原始状态，不安全 |
| **DPAPI（选中）** | 否 | 否 | 零操作，防文件泄露 |
| 主口令 + AES/PBKDF2 | 是 | 是（每次启动要输） | 便携，但麻烦 |

这个工具本来就是「装在自己电脑上长期挂着」的，不需要跨机器携带凭据，所以 DPAPI 更合适。

### 7.2 实现

```powershell
Add-Type -AssemblyName System.Security   # PS 5.1 不会自动加载

$enc = [System.Security.Cryptography.ProtectedData]::Protect(
    [Text.Encoding]::UTF8.GetBytes($plain), $null,
    [System.Security.Cryptography.DataProtectionScope]::CurrentUser)
' dpapi:' + [Convert]::ToBase64String($enc)
```

存进去的样子：

```json
"usernameEnc":    "dpapi:（密文，约 300 字符）",
"passwordEnc":    "dpapi:（密文，约 300 字符）",
"autoCookiesEnc": "dpapi:（密文，约 300 字符）"
```

> 加密结果是 Windows 数据保护 API 的二进制块，Base64 后以 `dpapi:` 开头。
> 同一份明文每次加密得到的字节都不同（含随机盐和机器/用户标识），所以这里不展示真实值。

11 位手机号加密后约 308 字符，所以 `config.json` 会从约 0.9 KB 涨到约 1.6 KB。

### 7.3 设计要点

**前缀 `dpapi:` 是格式标记**
解密函数看到不带前缀的值就原样返回，所以旧的明文配置仍然能读——这是自动迁移的基础。

**加密失败退回明文**
`Protect-Text` 捕获异常后返回原文而不是抛错。宁可不加密，也不能让用户因为加密失败而登不上。

**读取时把明文写回内存对象**
`Get-RawConfig` 解密后直接覆盖 `$cfg.username` / `$cfg.password` / `$cfg.autoCookies`，上层代码完全不用改——它们一直以为读到的就是明文。

**迁移只由面板做**
守护每轮也读配置，两边都写就会打架。所以迁移放在面板启动时（配置的唯一写入方）。

**注意：改完加密必须重启守护**
守护如果是改动之前启动的旧进程，它读不懂新的加密字段，表现为「尚未配置手机号」。