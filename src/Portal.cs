using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace CmccEdu
{
    /// <summary>登录过程中需要用户输入时的回调。守护进程传 null（永不打扰用户）。</summary>
    internal interface ILoginPrompter
    {
        /// <summary>让用户输入短信/图形验证码。返回 null 表示用户取消或超时。</summary>
        string AskCode(string hint, bool isSms);
        /// <summary>图形验证码：展示图片并让用户输入。返回 null 表示取消。</summary>
        string AskCaptcha(byte[] imageBytes);
    }

    internal sealed class LoginResult
    {
        public bool Ok;
        public string Kind;     // ok / no-cookie / no-ip / http / not-recognized / rejected / offline / need-manual
        public string Message;
        public string Mode;     // 本次登录实际使用的方式：dynamic / static
        public string AutoCookies;  // 本次登录服务端新下发的免密 cookie（"k=v; k=v"），没有则为 null
        public DateTime? AutoCookiesExpire;   // 这批 cookie 的到期时间（服务端给的到期日）

        public static LoginResult Fail(string kind, string message)
        {
            return new LoginResult { Ok = false, Kind = kind, Message = message };
        }

        public static LoginResult Success(string message)
        {
            return new LoginResult { Ok = true, Kind = "ok", Message = message };
        }
    }

    /// <summary>
    /// CMCC-EDU Portal 认证协议。所有细节都来自旧版 PowerShell 实现与 DESIGN.md，
    /// 改动这里之前请先看文档里的抓包结论。
    /// </summary>
    internal sealed class Portal : IDisposable
    {
        private readonly CookieContainer _cookies = new CookieContainer();
        private string _portalBase;   // 空值时从重定向里自动发现（见 EnsurePortalBase）
        private readonly string _ssid;
        private string _acName;
        private string _userAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) CmccEduGuard/2.0";

        public Portal(string portalBase, string ssid, string acName)
        {
            _portalBase = (portalBase ?? "").TrimEnd('/');
            _ssid = ssid;
            _acName = acName;
        }

        /// <summary>
        /// config 里没填 portal 时，从实际访问到的地址推断出 Portal 根地址。
        /// 认证流程本来就会跟着 AC 的 302 走，所以地址是现成的，不必硬编码。
        /// </summary>
        private void EnsurePortalBase(string url)
        {
            if (!string.IsNullOrEmpty(_portalBase)) return;
            if (string.IsNullOrEmpty(url)) return;

            // 只认「认证页」的地址。已联网时请求百度也会成功，但那不是 Portal，
            // 认错了会把后续请求全打到百度上去（实测踩过）。
            if (url.IndexOf("wlanuserip=", StringComparison.OrdinalIgnoreCase) < 0) return;

            try
            {
                var u = new Uri(url);
                string host = u.Scheme + "://" + u.Authority;
                _portalBase = host.TrimEnd('/');
                AppLog.Write("自动识别 Portal 地址: " + _portalBase + "（来自 " + u.AbsolutePath + "）", "NET");
            }
            catch { /* 不是合法 URL 就继续用空值，后面会明确报错 */ }
        }

        public CookieContainer Cookies { get { return _cookies; } }

        // ---------------------------------------------------------------- HTTP

        private HttpWebRequest NewRequest(string url, string method)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = method;
            req.CookieContainer = _cookies;
            req.UserAgent = _userAgent;
            req.AllowAutoRedirect = true;
            req.MaximumAutomaticRedirections = 10;
            req.Timeout = 20000;
            req.ReadWriteTimeout = 20000;
            req.KeepAlive = true;
            return req;
        }

        private string GetText(string url, out string finalUrl)
        {
            string text = null;
            finalUrl = url;
            try
            {
                var req = NewRequest(url, "GET");
                using (var resp = (HttpWebResponse)req.GetResponse())
                {
                    finalUrl = resp.ResponseUri != null ? resp.ResponseUri.AbsoluteUri : url;
                    using (var reader = new StreamReader(resp.GetResponseStream(), DetectEncoding(resp)))
                        text = reader.ReadToEnd();
                }
            }
            catch (WebException ex)
            {
                AppLog.Write("请求失败 " + url + " : " + ex.Message, "ERR");
            }
            return text;
        }

        private static Encoding DetectEncoding(HttpWebResponse resp)
        {
            // Portal 页面是 gb2312，按 charset 来；识别不了就用 GBK 兜底
            try
            {
                if (!string.IsNullOrEmpty(resp.CharacterSet)) return Encoding.GetEncoding(resp.CharacterSet);
            }
            catch { /* charset 不认识就用默认的 */ }
            try { return Encoding.GetEncoding("gb2312"); }
            catch { return Encoding.UTF8; }
        }

        private string PostForm(string url, Dictionary<string, string> form, out string finalUrl)
        {
            finalUrl = url;
            var sb = new StringBuilder();
            foreach (var kv in form)
            {
                if (sb.Length > 0) sb.Append('&');
                sb.Append(Uri.EscapeDataString(kv.Key)).Append('=').Append(Uri.EscapeDataString(kv.Value ?? ""));
            }
            byte[] data = Encoding.UTF8.GetBytes(sb.ToString());

            try
            {
                var req = NewRequest(url, "POST");
                req.ContentType = "application/x-www-form-urlencoded";
                req.ContentLength = data.Length;
                using (var s = req.GetRequestStream()) s.Write(data, 0, data.Length);

                using (var resp = (HttpWebResponse)req.GetResponse())
                {
                    finalUrl = resp.ResponseUri != null ? resp.ResponseUri.AbsoluteUri : url;
                    using (var reader = new StreamReader(resp.GetResponseStream(), DetectEncoding(resp)))
                        return reader.ReadToEnd();
                }
            }
            catch (WebException ex)
            {
                var http = ex.Response as HttpWebResponse;
                if (http != null)
                {
                    AppLog.Write("提交被拒 HTTP " + (int)http.StatusCode, "ERR");
                    return null;
                }
                AppLog.Write("提交失败: " + ex.Message, "ERR");
                return null;
            }
        }

        private byte[] GetBytes(string url)
        {
            try
            {
                var req = NewRequest(url, "GET");
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var ms = new MemoryStream())
                {
                    resp.GetResponseStream().CopyTo(ms);
                    var bytes = ms.ToArray();
                    return bytes.Length > 400 ? bytes : null;
                }
            }
            catch (WebException ex)
            {
                AppLog.Write("取图片失败: " + ex.Message, "ERR");
                return null;
            }
        }

        // ---------------------------------------------------------------- 解析

        private static string FindFrameUrl(string html)
        {
            if (string.IsNullOrEmpty(html)) return null;
            var m = Regex.Match(html, "<frame[^>]*\\ssrc=\"([^\"]*index\\.jsp\\?paramStr=[^\"]*)\"");
            if (!m.Success) m = Regex.Match(html, "src=\"([^\"]*index\\.jsp\\?paramStr=[^\"]*)\"");
            if (!m.Success) m = Regex.Match(html, "(style/[^\"'\\s]*index\\.jsp\\?paramStr=[^\"'\\s]*)");
            return m.Success ? m.Groups[1].Value : null;
        }

        private static string FindMainFrameUrl(string html)
        {
            if (string.IsNullOrEmpty(html)) return null;
            var m = Regex.Match(html, "<frame[^>]*name=\"mainFrame\"[^>]*src=\"([^\"]*)\"");
            if (!m.Success) m = Regex.Match(html, "src=\"([^\"]*(?:authServlet|index\\.jsp)\\?paramStr=[^\"]*)\"");
            return m.Success ? (m.Groups[1].Value.Replace("&amp;", "&")) : null;
        }

        private static string FindParamStr(string html)
        {
            if (string.IsNullOrEmpty(html)) return null;
            var m = Regex.Match(html, "name=\"paramStr\"[^>]*value=\"([^\"]*)\"");
            if (!m.Success) m = Regex.Match(html, "id=\"paramStr\"[^>]*value=\"([^\"]*)\"");
            return m.Success ? WebUtility.HtmlDecode(m.Groups[1].Value) : null;
        }

        private static string JoinUrl(string baseUrl, string relative)
        {
            try { return new Uri(new Uri(baseUrl), relative).AbsoluteUri; }
            catch { return relative; }
        }

        private static string HtmlToText(string html)
        {
            if (string.IsNullOrEmpty(html)) return "";
            var t = Regex.Replace(html, "<script[\\s\\S]*?</script>", " ");
            t = Regex.Replace(t, "<[^>]+>", " ");
            t = t.Replace("&nbsp;", " ");
            return Regex.Replace(t, "\\s+", " ").Trim();
        }

        // ---------------------------------------------------------------- cookie 免密

        private sealed class CookieSession
        {
            public bool Ok;
            public string Kind;
            public string Message;
            public string Base;
            public string Landing;
            public string FrameRel;
        }

        private CookieSession OpenCookieSession(LoginSettings s)
        {
            var result = new CookieSession();
            var pairs = ParseCookieHeader(s.AutoCookies);
            if (pairs.Count == 0)
            {
                result.Kind = "no-cookie"; result.Message = "没有保存自动登录 cookie";
                return result;
            }

            // 配置里没填 portal 或 acName 时，先空跑一次请求：
            // 未认证状态下 AC 会 302 到认证页，地址里天然带着 wlanuserip 和 wlanacname。
            if (string.IsNullOrEmpty(_portalBase) || string.IsNullOrEmpty(_acName))
            {
                string trig = string.IsNullOrWhiteSpace(s.TriggerUrl) ? "http://www.baidu.com/" : s.TriggerUrl;
                string trigFinal;
                GetText(trig, out trigFinal);

                EnsurePortalBase(trigFinal);

                if (string.IsNullOrEmpty(_acName))
                {
                    var ma = Regex.Match(trigFinal ?? "", "wlanacname=([^&]+)");
                    if (ma.Success)
                    {
                        _acName = Uri.UnescapeDataString(ma.Groups[1].Value);
                        AppLog.Write("自动识别 AC 名: " + _acName, "NET");
                    }
                }

                if (string.IsNullOrEmpty(_portalBase))
                {
                    result.Kind = "no-portal";
                    result.Message = "没能识别 Portal 地址，请在配置里填写 portal";
                    return result;
                }
            }

            string baseUrl = _portalBase;
            var uri = new Uri(baseUrl);
            foreach (var kv in pairs)
            {
                try
                {
                    _cookies.Add(uri, new Cookie(kv.Key, kv.Value, "/", uri.Host) { HttpOnly = false });
                }
                catch (Exception ex)
                {
                    // 单个 cookie 名字/值非法不影响其它 cookie
                    AppLog.Write("丢弃无效 cookie " + kv.Key + ": " + ex.Message, "COOKIE");
                }
            }

            string ip = Network.GetSsidAdapterIp(_ssid) ?? Network.GetDefaultRouteIp();
            if (string.IsNullOrEmpty(ip))
            {
                result.Kind = "no-ip"; result.Message = "拿不到本机 IP（网络可能还没就绪）";
                return result;
            }

            string landing = string.Format("{0}/?wlanuserip={1}&wlanacname={2}&ssid={3}",
                baseUrl, Uri.EscapeDataString(ip), Uri.EscapeDataString(_acName ?? ""), Uri.EscapeDataString(_ssid ?? ""));
            string finalUrl;
            string html = GetText(landing, out finalUrl);
            if (html == null)
            {
                result.Kind = "http"; result.Message = "打开认证页失败";
                return result;
            }

            // Portal 可能被重定向到别的主机，以实际落点为准
            try
            {
                var fu = new Uri(finalUrl);
                if (fu.GetLeftPart(UriPartial.Authority).Length > 0)
                    baseUrl = fu.GetLeftPart(UriPartial.Authority);
            }
            catch { /* 落点不是合法 URL 时沿用原 base */ }

            string frameRel = FindMainFrameUrl(html);
            if (string.IsNullOrEmpty(frameRel))
            {
                result.Kind = "http"; result.Message = "认证页里没找到主框架";
                return result;
            }

            result.Ok = true; result.Kind = "ok";
            result.Base = baseUrl; result.Landing = landing; result.FrameRel = frameRel;
            return result;
        }

        /// <summary>cookie 免密登录：一个 GET 就完成。返回 Kind 区分失败原因。</summary>
        public LoginResult CookieLogin(LoginSettings s)
        {
            var sess = OpenCookieSession(s);
            if (!sess.Ok) return LoginResult.Fail(sess.Kind, sess.Message);
            EnsurePortalBase(sess.Landing);

            // 服务端认 cookie 时主框架直接是 authServlet；返回 index.jsp 说明没认
            if (sess.FrameRel.IndexOf("index.jsp", StringComparison.OrdinalIgnoreCase) >= 0)
                return LoginResult.Fail("not-recognized", "服务端没认自动登录凭据（返回的是登录页）");

            string frameUrl = JoinUrl(sess.Landing, sess.FrameRel);
            string finalUrl;
            string html = GetText(frameUrl, out finalUrl);
            if (html == null) return LoginResult.Fail("http", "自动登录请求失败");

            if (finalUrl.IndexOf("login_fail", StringComparison.OrdinalIgnoreCase) >= 0)
                return LoginResult.Fail("rejected", "自动登录被拒（login_fail）");

            for (int i = 0; i < 8; i++)
            {
                if (Network.IsOnline(4)) return LoginResult.Success("cookie 自动登录成功");
                Thread.Sleep(800);
            }
            return LoginResult.Fail("offline", "自动登录后仍无法上网");
        }

        // ---------------------------------------------------------------- 完整登录

        /// <summary>
        /// 完整认证流程。mode: "cookie" / "sms" / "password"。
        /// prompter 用于需要用户输入验证码时；守护进程传 null，此时不会发短信。
        /// </summary>
        public LoginResult Login(LoginSettings s, string mode, ILoginPrompter prompter)
        {
            // 先试 cookie（除非用户明确要求短信/密码）
            if (mode == "cookie" && !string.IsNullOrWhiteSpace(s.AutoCookies))
            {
                var rc = CookieLogin(s);
                if (rc.Ok) return rc;
                bool credentialGone = rc.Kind == "not-recognized" || rc.Kind == "rejected";
                if (!credentialGone)
                    return LoginResult.Fail("retry", "暂时无法认证：" + rc.Message + "，稍后自动重试");
                AppLog.Write("凭据失效：" + rc.Message + "，改用账号登录", "COOKIE");
            }

            string trigger = string.IsNullOrWhiteSpace(s.TriggerUrl) ? "http://www.baidu.com/" : s.TriggerUrl;
            string finalUrl;
            string html = GetText(trigger, out finalUrl) ?? "";
            string frameRel = FindFrameUrl(html);

            // 有些 AC 返回 JS 跳转页
            if (string.IsNullOrEmpty(frameRel) && html.Length > 0)
            {
                var mj = Regex.Match(html, "location\\.href\\s*=\\s*[\"'](http[^\"']+)[\"']");
                if (mj.Success)
                {
                    string jump = mj.Groups[1].Value;
                    html = GetText(jump, out finalUrl) ?? "";
                    frameRel = FindFrameUrl(html);
                }
            }

            // 没落到认证页：自己拼 Portal 地址
            if (string.IsNullOrEmpty(frameRel))
            {
                if (Network.IsOnline()) return LoginResult.Success("已联网，无需认证");

                // 配置里没填 portal 时，从刚才重定向到的地址推断
                EnsurePortalBase(finalUrl);
                if (string.IsNullOrEmpty(_portalBase))
                    return LoginResult.Fail("http", "没能识别 Portal 地址，请在配置里填写 portal");

                string ip = Network.GetSsidAdapterIp(_ssid) ?? Network.GetDefaultRouteIp();
                if (string.IsNullOrEmpty(ip)) return LoginResult.Fail("no-ip", "拿不到本机 IPv4 地址");

                string acName = _acName;
                try
                {
                    var mu = Regex.Match(finalUrl ?? "", "wlanuserip=([^&]+)");
                    if (mu.Success) ip = Uri.UnescapeDataString(mu.Groups[1].Value);
                    var ma = Regex.Match(finalUrl ?? "", "wlanacname=([^&]+)");
                    if (ma.Success) acName = Uri.UnescapeDataString(ma.Groups[1].Value);
                }
                catch { /* 地址里没有这两个参数就用配置里的 */ }

                string direct = string.Format("{0}/?wlanuserip={1}&wlanacname={2}&ssid={3}",
                    _portalBase, Uri.EscapeDataString(ip), Uri.EscapeDataString(acName ?? ""), Uri.EscapeDataString(_ssid ?? ""));
                html = GetText(direct, out finalUrl) ?? "";
                frameRel = FindFrameUrl(html);
                if (string.IsNullOrEmpty(frameRel))
                    return LoginResult.Fail("http", "没找到认证页面（落点 " + finalUrl + "）");
            }

            // 加载 index.jsp 取 paramStr
            string frameUrl2 = JoinUrl(finalUrl, frameRel);
            string frameFinal;
            string frameHtml = GetText(frameUrl2, out frameFinal) ?? "";
            string paramStr = FindParamStr(frameHtml);
            if (string.IsNullOrEmpty(paramStr))
            {
                var m2 = Regex.Match(frameRel, "paramStr=([^&\"'\\s]+)");
                if (m2.Success) paramStr = Uri.UnescapeDataString(m2.Groups[1].Value);
            }
            if (string.IsNullOrEmpty(paramStr))
                return LoginResult.Fail("http", "无法从认证页取到动态 paramStr");

            // 组装提交体
            var body = new Dictionary<string, string>
            {
                { "paramStr", paramStr },
                { "UserType", "1" },
                { "province", "" },
                { "pwdType", "1" },
                { "UserName", s.Username ?? "" },
                { "PassWord", s.Password ?? "" },
                { "autoload", s.Autoload == "test" ? "test" : "checked" }
            };

            // 图形验证码（连续失败后 Portal 才会要求）
            if (CaptchaNeeded(s.Username))
            {
                if (prompter == null) return LoginResult.Fail("need-manual", "需要图形验证码，但当前是自动流程");

                for (int round = 1; round <= 5; round++)
                {
                    byte[] img = GetBytes(_portalBase + "/createVerifycode?" + new Random().Next(100000, 999999));
                    if (img == null) return LoginResult.Fail("http", "取图形验证码失败");

                    string code = prompter.AskCaptcha(img);
                    if (code == null) return LoginResult.Fail("need-manual", "验证码输入已取消");

                    if (CheckCaptchaCode(code))
                    {
                        body["verifycode"] = code;
                        AppLog.Write("验证码校验通过", "CAPTCHA");
                        break;
                    }
                    AppLog.Write("验证码不正确，重新取一张", "CAPTCHA");
                    if (round == 5) return LoginResult.Fail("need-manual", "图形验证码连续错误");
                }
            }

            // 动态密码：需要用户手动触发才发短信
            if (s.LoginMode == "dynamic")
            {
                if (prompter == null)
                {
                    AppLog.Write("动态密码模式：自动流程不发短信，需要用户手动触发", "SMS");
                    return LoginResult.Fail("need-manual", "动态密码模式需要手动触发登录");
                }

                string smsReply = RequestSmsCode(s.Username);
                Status.Write("captcha", "等待输入短信验证码", "captcha", 0);
                string code = prompter.AskCode(string.IsNullOrEmpty(smsReply) ? "请输入手机收到的短信验证码" : smsReply, true);
                if (code == null) return LoginResult.Fail("need-manual", "短信验证码输入已取消");

                body["pwdType"] = "2";
                body["PassWord"] = code;
                AppLog.Write("已获取动态密码，提交认证", "SMS");
            }

            string respFinal;
            string respHtml = PostForm(_portalBase + "/authServlet", body, out respFinal);
            if (respHtml == null) return LoginResult.Fail("http", "提交认证请求失败");

            DumpAuthExchange(respHtml, respFinal);

            if ((respFinal ?? "").IndexOf("login_fail", StringComparison.OrdinalIgnoreCase) < 0)
            {
                for (int i = 0; i < 8; i++)
                {
                    if (Network.IsOnline(4))
                    {
                        var done = LoginResult.Success("认证成功，外网已放行");
                        // 完整登录时服务端会重新下发一批 AUTOAUTH_* cookie，
                        // 有效期是「此刻 + 90 天」。把它们带出去，让上层存回配置，
                        // 下次就能用最新的一批（滚动续期）。
                        DateTime? exp;
                        done.AutoCookies = CollectAutoAuthCookies(out exp);
                        done.AutoCookiesExpire = exp;
                        return done;
                    }
                    Thread.Sleep(900);
                }
            }

            string reason = ExtractFailReason(respHtml, respFinal);
            return LoginResult.Fail("rejected", "认证失败: " + reason);
        }

        private static string ExtractFailReason(string html, string finalUrl)
        {
            string text = HtmlToText(html);
            string reason = "";
            var m = Regex.Match(text, "登录失败[：:]\\s*(.{0,200})");
            if (m.Success) reason = m.Groups[1].Value.Trim();
            if (reason.Length == 0)
            {
                var m2 = Regex.Match(text, ".{0,30}(?:密码|账号|用户名).{0,40}");
                if (m2.Success && Regex.IsMatch(m2.Value, "错误|不正确|失败")) reason = m2.Value.Trim();
            }
            if (reason.Length == 0 && (finalUrl ?? "").IndexOf("login_fail", StringComparison.OrdinalIgnoreCase) >= 0)
                reason = "Portal 返回 login_fail.jsp";
            if (reason.Length == 0) reason = "认证后外网仍未放行";
            return reason.Length > 200 ? reason.Substring(0, 200) + "..." : reason;
        }

        // ---------------------------------------------------------------- 验证码 / 短信

        private bool CaptchaNeeded(string username)
        {
            string finalUrl;
            string url = string.Format("{0}/queryServlet?operate=authfailcount&username={1}&t={2}",
                _portalBase, Uri.EscapeDataString(username ?? ""), new Random().Next());
            string text = GetText(url, out finalUrl);
            return text != null && text.Trim().Equals("false", StringComparison.OrdinalIgnoreCase);
        }

        private bool CheckCaptchaCode(string code)
        {
            string finalUrl;
            string url = string.Format("{0}/queryServlet?operate=checkverifycode&verifycode={1}&defaultIndex=1&t={2}",
                _portalBase, Uri.EscapeDataString(code ?? ""), new Random().Next());
            string text = GetText(url, out finalUrl);
            return text != null && text.Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>请求短信验证码。返回服务器提示文本（用于展示给用户）。</summary>
        private string RequestSmsCode(string username)
        {
            string finalUrl;
            string url = string.Format("{0}/getPassword.jsp?command=getpassword&phonenumber={1}&t={2}",
                _portalBase, Uri.EscapeDataString(username ?? ""), new Random().Next(1000, 9999));
            string reply = GetText(url, out finalUrl);
            string text = HtmlToText(reply);
            AppLog.Write("已请求动态密码，服务器返回: " + text, "SMS");
            return text;
        }

        // ---------------------------------------------------------------- 下线

        public LoginResult Logout(LoginSettings s)
        {
            var sess = OpenCookieSession(s);
            if (!sess.Ok)
            {
                if (sess.Kind == "no-cookie")
                    return LoginResult.Fail("no-cookie", "没有保存自动登录 cookie，无法下线");
                return LoginResult.Fail(sess.Kind, sess.Message);
            }

            string frameUrl = JoinUrl(sess.Landing, sess.FrameRel);
            string finalUrl;
            string html = GetText(frameUrl, out finalUrl);
            if (html == null) return LoginResult.Fail("http", "自动登录失败，拿不到下线令牌");

            var pm = Regex.Match(finalUrl ?? "", "paramStr=([^&]+)");
            if (!pm.Success) return LoginResult.Fail("http", "拿不到下线用的 paramStr（当前可能未登录）");
            string ps = pm.Groups[1].Value;

            string logoutFinal;
            string logoutUrl = sess.Base + "/logoutServlet?paramStr=" + ps;
            string text = GetText(logoutUrl, out logoutFinal);
            if (text == null) return LoginResult.Fail("http", "下线请求失败");

            Thread.Sleep(3000);
            if (!Network.IsOnline()) return LoginResult.Success("已下线，外网已断开");

            string snippet = HtmlToText(text);
            if (snippet.Length > 80) snippet = snippet.Substring(0, 80);
            return LoginResult.Fail("still-online", "下线请求已发送，但外网仍可用（" + snippet + "）");
        }

        // ---------------------------------------------------------------- 其它

        /// <summary>把本次认证的响应头和 cookie 落到 auth_headers.log，方便排查免密登录。</summary>
        private void DumpAuthExchange(string html, string finalUrl)
        {
            try
            {
                bool full = false;   // config 里 dumpCookies 打开时才输出完整 cookie
                var cfg = Config.Load();
                full = string.Equals(cfg.GetString("dumpCookies"), "true", StringComparison.OrdinalIgnoreCase);

                string safeUrl = finalUrl ?? "";
                if (!full) safeUrl = Regex.Replace(safeUrl, "(paramStr=)[^&]+", "$1…（已打码）");

                var sb = new StringBuilder();
                sb.AppendLine("===== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  落点: " + safeUrl);

                var uri = new Uri(_portalBase);
                foreach (Cookie ck in _cookies.GetCookies(uri))
                {
                    string exp = ck.Expires == DateTime.MinValue ? "会话级" : ck.Expires.ToString("yyyy-MM-dd HH:mm:ss");
                    string show = full
                        ? ck.Value
                        : (ck.Value.Length > 4 ? ck.Value.Substring(0, 4) + "…（" + ck.Value.Length + "字符）" : "（" + ck.Value.Length + "字符）");
                    sb.AppendLine("C  " + ck.Name + " = " + show + "   [path=" + ck.Path + " expires=" + exp + " httpOnly=" + ck.HttpOnly + "]");
                }

                string path = Path.Combine(AppEnv.Root, "auth_headers.log");
                File.AppendAllText(path, sb.ToString(), new UTF8Encoding(false));

                // 这个文件会一直增长，只保留最近的内容
                var fi = new FileInfo(path);
                if (fi.Length > 64 * 1024)
                {
                    var lines = File.ReadAllLines(path, Encoding.UTF8);
                    int keep = Math.Min(lines.Length, 300);
                    var tail = new string[keep];
                    Array.Copy(lines, lines.Length - keep, tail, 0, keep);
                    File.WriteAllText(path, string.Join("\r\n", tail) + "\r\n", new UTF8Encoding(false));
                }
            }
            catch (Exception ex)
            {
                // 排查用的辅助日志，失败不影响认证
                AppLog.Write("写认证日志失败: " + ex.Message, "ERR");
            }
        }

        /// <summary>把本次会话里服务端下发的 AUTOAUTH_* cookie 拼成 "k=v; k=v"。</summary>
        private string CollectAutoAuthCookies(out DateTime? expire)
        {
            expire = null;   // out 参数必须先赋值，否则 catch 分支编译不过
            try
            {
                var parts = new List<string>();
                DateTime? maxExp = null;
                var uri = new Uri(_portalBase);
                foreach (Cookie ck in _cookies.GetCookies(uri))
                {
                    if (!ck.Name.StartsWith("AUTOAUTH_", StringComparison.Ordinal)) continue;
                    if (string.IsNullOrEmpty(ck.Value)) continue;
                    parts.Add(ck.Name + "=" + ck.Value);
                    if (ck.Expires != DateTime.MinValue && (!maxExp.HasValue || ck.Expires > maxExp.Value))
                        maxExp = ck.Expires;
                }
                expire = maxExp;
                return parts.Count > 0 ? string.Join("; ", parts) : null;
            }
            catch (Exception ex)
            {
                AppLog.Write("收集新 cookie 失败: " + ex.Message, "ERR");
                expire = null;
                return null;
            }
        }

        private static Dictionary<string, string> ParseCookieHeader(string raw)
        {
            var pairs = new Dictionary<string, string>();
            if (string.IsNullOrWhiteSpace(raw)) return pairs;
            foreach (var part in raw.Split(';'))
            {
                var t = part.Trim();
                int eq = t.IndexOf('=');
                if (eq <= 0) continue;
                pairs[t.Substring(0, eq).Trim()] = t.Substring(eq + 1).Trim();
            }
            return pairs;
        }

        public void Dispose()
        {
            // CookieContainer 没有需要释放的资源；保留接口以便将来换成 HttpClient
        }
    }
}
