using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace CmccEdu
{
    /// <summary> 用户操作与业务编排。</summary>
    internal sealed partial class MainForm
    {
        private void OnShown(object sender, EventArgs e)
        {
            AppLog.WriteErrorLog("面板已显示");

            // 控件都建好了，统一把标签/复选框染成卡片色
            PaintCardBackcolors();

            // 标题栏也刷成白色，和内容连成一片
            TitleBarTheme.Apply(Handle);



            try { InitTray(); }
            catch (Exception ex) { AppLog.WriteErrorLog("创建托盘失败: " + ex.Message); }

            // 记录日志基线：只显示本次启动之后的内容
            try { _logOffset = File.Exists(AppEnv.LogPath) ? new FileInfo(AppEnv.LogPath).Length : 0; }
            catch { _logOffset = 0; }
            _lastLogLen = -1;
            RefreshUi();

            _uiTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            _uiTimer.Tick += (s2, e2) =>
            {
                try { RefreshUi(); }
                catch (Exception ex) { AppLog.WriteErrorLog("刷新界面异常: " + ex.Message); }
            };
            _uiTimer.Start();

            // 打开面板就自动开启守护
            try
            {
                if (!Guard.IsRunning())
                {
                    Guard.Start();
                    AppLog.WriteErrorLog("面板启动，已自动开启守护");
                }
            }
            catch (Exception ex) { AppLog.WriteErrorLog("自动开启守护失败: " + ex.Message); }

            if (AppEnv.StartHidden)
            {
                _quietStart = true;
                Hide();
            }
        }

        private void ShowPanel()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        private void OnTrayExit()
        {
            var r = MessageBox.Show(
                "要同时关闭守护进程吗？\r\n\r\n是   → 关闭守护，并退出面板\r\n否   → 只退出面板，守护继续在后台运行\r\n取消 → 什么都不做",
                "退出面板", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (r == DialogResult.Cancel) return;

            if (r == DialogResult.Yes)
            {
                // 退出时停守护：失败也不影响退出，记一行日志
                SafeDelete.Try(delegate { Guard.Stop(); }, "退出时停止守护", "SYS");
                AppLog.WriteErrorLog("退出面板并关闭守护");
            }
            else AppLog.WriteErrorLog("退出面板，守护继续运行");

            ExitPanel();
        }

        private void ExitPanel()
        {
            _reallyExit = true;
            SafeDelete.Try(delegate { if (_uiTimer != null) _uiTimer.Stop(); }, "停止界面定时器", "SYS");
            SafeDelete.Try(delegate { if (_tray != null) { _tray.Visible = false; _tray.Dispose(); } }, "销毁托盘图标", "SYS");
            Close();
        }

        private void OnFormClosing(object sender, FormClosingEventArgs e)
        {
            if (_reallyExit) return;
            e.Cancel = true;
            try
            {
                Hide();
                if (!_quietStart && _tray != null)
                    _tray.ShowBalloonTip(2500, "CMCC-EDU 网络守护", "面板已收进托盘，守护仍在后台运行。", ToolTipIcon.Info);
            }
            catch (Exception ex) { AppLog.WriteErrorLog("收进托盘失败: " + ex.Message); }
        }

        private void LoadConfigIntoUi()
        {
            Config cfg;
            try { cfg = Config.Load(); }
            catch (Exception ex)
            {
                AppLog.WriteErrorLog("读取配置失败: " + ex.Message);
                _loginMode = "dynamic";
                SelectMode("dynamic");
                return;
            }

            _txtUser.Text = Config.Unprotect(cfg.GetString("usernameEnc"));
            if (string.IsNullOrEmpty(_txtUser.Text)) _txtUser.Text = cfg.GetString("username");

            string pass = Config.Unprotect(cfg.GetString("passwordEnc"));
            if (string.IsNullOrEmpty(pass)) pass = cfg.GetString("password");
            _txtPass.Text = pass;

            string mode = cfg.GetString("loginMode");
            if (string.IsNullOrWhiteSpace(mode)) mode = "dynamic";
            _loginMode = mode;
            SelectMode(mode);
            ApplyLoginMode();

            int interval = cfg.GetInt("interval", 60);
            if (interval <= 0) interval = 60;
            int idx = 0;
            int[] secs = { 5, 10, 15, 30, 60, 120, 300 };
            for (int i = 0; i < secs.Length; i++) if (secs[i] == interval) { idx = i; break; }
            _cmbInterval.SelectedIndex = idx;

            _chkAutoLogin.Checked = !string.Equals(cfg.GetString("autoload"), "test", StringComparison.OrdinalIgnoreCase);
            // 初始化控件会触发 CheckedChanged/SelectedIndexChanged，
            // 必须屏蔽掉，否则"只读"状态也会去重写计划任务
            _suppressAutoEvent = true;
            try
            {
                _chkAutoGuard.Checked = Autostart.Exists(Autostart.TaskGuard);
                _chkAutoGui.Checked = Autostart.Exists(Autostart.TaskClient);
                _cmbGuiMode.SelectedIndex = string.Equals(cfg.GetString("guiStartMode"), "taskbar", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
            }
            finally { _suppressAutoEvent = false; }
        }

        private void SelectMode(string mode)
        {
            _suppressModeEvent = true;
            try { _cmbMode.SelectedIndex = mode == "dynamic" ? 1 : 0; }
            finally { _suppressModeEvent = false; }
        }

        private void OnModeChanged(object sender, EventArgs e)
        {
            if (_suppressModeEvent) return;
            _loginMode = _cmbMode.SelectedIndex == 1 ? "dynamic" : "static";
            ApplyLoginMode();
            SaveLoginMode();
        }

        private void SaveLoginMode()
        {
            try
            {
                var cfg = Config.Load();
                cfg.SetString("loginMode", _loginMode);
                cfg.Save();
            }
            catch (Exception ex) { AppLog.WriteErrorLog("保存登录方式失败: " + ex.Message); }
        }

        private void OnSaveClick(object sender, EventArgs e)
        {
            string user = _txtUser.Text.Trim();
            string pass = _txtPass.Text;
            if (user.Length == 0)
            {
                MessageBox.Show("请先填写手机号。", "CMCC-EDU 网络守护", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                var cfg = Config.Load();
                cfg.SetSecret("username", "usernameEnc", user);
                cfg.SetSecret("password", "passwordEnc", pass);
                cfg.SetString("loginMode", _loginMode);
                cfg.SetString("autoload", _chkAutoLogin.Checked ? "checked" : "test");
                cfg.Save();
                AppLog.Write("已保存账号", "CFG");
                MessageBox.Show("已保存。", "CMCC-EDU 网络守护", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                AppLog.WriteErrorLog("保存账号失败: " + ex.Message);
                MessageBox.Show("保存失败：" + ex.Message, "CMCC-EDU 网络守护", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void OnIntervalChanged(object sender, EventArgs e)
        {
            int[] secs = { 5, 10, 15, 30, 60, 120, 300 };
            int idx = Math.Max(0, Math.Min(_cmbInterval.SelectedIndex, secs.Length - 1));
            try
            {
                var cfg = Config.Load();
                cfg.SetString("interval", secs[idx].ToString());
                cfg.Save();
            }
            catch (Exception ex) { AppLog.WriteErrorLog("保存检测间隔失败: " + ex.Message); }
        }

        private void OnAutoGuardChanged(object sender, EventArgs e)
        {
            if (_suppressAutoEvent) return;
            try
            {
                if (!Autostart.SetGuard(_chkAutoGuard.Checked))
                {
                    MessageBox.Show("设置开机启动失败，请查看 gui_error.log。", "CMCC-EDU 网络守护", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex) { AppLog.WriteErrorLog("设置守护自启失败: " + ex.Message); }
        }

        private void OnAutoGuiChanged(object sender, EventArgs e)
        {
            if (_suppressAutoEvent) return;
            try
            {
                if (!Autostart.SetClient(_chkAutoGui.Checked, _cmbGuiMode.SelectedIndex == 0))
                {
                    MessageBox.Show("设置开机启动失败，请查看 gui_error.log。", "CMCC-EDU 网络守护", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            catch (Exception ex) { AppLog.WriteErrorLog("设置面板自启失败: " + ex.Message); }
        }

        private void OnGuiModeChanged(object sender, EventArgs e)
        {
            if (_suppressAutoEvent) return;
            string mode = _cmbGuiMode.SelectedIndex == 1 ? "taskbar" : "tray";
            try
            {
                var cfg = Config.Load();
                cfg.SetString("guiStartMode", mode);
                cfg.Save();
            }
            catch (Exception ex) { AppLog.WriteErrorLog("保存面板启动方式失败: " + ex.Message); }

            // 已经建了自启任务的话，参数要跟着改
            if (_chkAutoGui.Checked) OnAutoGuiChanged(sender, e);
        }

        private void OnToggleGuardClick(object sender, EventArgs e)
        {
            try
            {
                if (Guard.IsRunning()) Guard.Stop(); else Guard.Start();
            }
            catch (Exception ex) { AppLog.WriteErrorLog("切换守护失败: " + ex.Message); }

            // 刚切换完，缓存立刻作废，界面要马上反映真实状态
            Guard.InvalidateRunningCache();
            RefreshUi();
        }

        private void OnCookieLoginClick(object sender, EventArgs e)
        {
            if (_txtUser.Text.Trim().Length == 0)
            {
                MessageBox.Show("请先填写手机号并保存账号。", "CMCC-EDU 网络守护", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            _btnCheck.Text = "cookies登录中…";
            RunLoginAsync("cookie");
        }

        private void OnSendCodeClick(object sender, EventArgs e)
        {
            if (_txtUser.Text.Trim().Length == 0)
            {
                MessageBox.Show("请先填写手机号并保存账号。", "CMCC-EDU 网络守护", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            var r = MessageBox.Show("将向 " + _txtUser.Text.Trim() + " 发送一条短信验证码。\r\n\r\n注意：Portal 对发码有 10 分钟冷却，重复点击不会立即发新码。\r\n\r\n确认发送吗？",
                "确认发送短信", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (r != DialogResult.Yes) return;

            SetLoginMode("dynamic");
            _btnSendCode.Text = "已请求，等待弹窗…";
            RunLoginAsync("sms");
        }

        private void OnStaticLoginClick(object sender, EventArgs e)
        {
            if (_txtUser.Text.Trim().Length == 0 || _txtPass.Text.Length == 0)
            {
                MessageBox.Show("请先填写手机号和密码并保存账号。", "CMCC-EDU 网络守护", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            SetLoginMode("static");
            _btnStaticLogin.Text = "登录中…";
            RunLoginAsync("password");
        }

        private void SetLoginMode(string mode)
        {
            if (_loginMode == mode) return;
            _loginMode = mode;
            SelectMode(mode);
            ApplyLoginMode();
            SaveLoginMode();
        }

        /// <summary>
        /// 在后台线程跑登录，对话框回到 UI 线程弹。
        /// 不再 spawn 子进程，省掉文件 IPC 和一类竞态。
        /// </summary>
        private void RunLoginAsync(string mode)
        {
            if (_loginBusy) return;

            LoginSettings settings;
            try
            {
                var cfg = Config.Load();
                // 用界面上的当前值，避免用户改了没保存就点登录
                cfg.SetSecret("username", "usernameEnc", _txtUser.Text.Trim());
                if (_loginMode == "static") cfg.SetSecret("password", "passwordEnc", _txtPass.Text);
                settings = cfg.GetLoginSettings();
            }
            catch (Exception ex)
            {
                RestoreLoginButtons();
                MessageBox.Show(ex.Message, "CMCC-EDU 网络守护", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            _loginBusy = true;
            SetLoginButtons(false);

            var prompter = new FormPrompter(this);
            var thread = new System.Threading.Thread(() =>
            {
                LoginResult result;
                try { result = LoginRunner.ManualLogin(settings, mode, prompter); }
                catch (Exception ex) { result = LoginResult.Fail("error", "登录异常：" + ex.Message); }

                try
                {
                    BeginInvoke(new Action(() => OnLoginFinished(result)));
                }
                catch (Exception ex)
                {
                    // 面板已经关了，没人接这个结果
                    AppLog.WriteErrorLog("回传登录结果失败: " + ex.Message);
                }
            });
            thread.IsBackground = true;
            thread.Start();
        }

        private void OnLoginFinished(LoginResult result)
        {
            RestoreLoginButtons();

            int interval = 60;
            try { interval = Config.Load().GetLoginSettings().Interval; } catch { /* 配置读不到就用默认值 */ }

            if (result.Ok)
            {
                AppLog.Write(result.Message, "AUTH");
                Status.Write("online", result.Message, "login_ok", interval, 0, result.Mode);
            }
            else
            {
                AppLog.WriteErrorLog("登录结果：" + result.Message);
                Status.Write("offline", result.Message, "login_fail", interval);
            }

            RefreshUi();

            if (!result.Ok && result.Kind != "no-cookie")
            {
                MessageBox.Show(result.Message, "登录结果", MessageBoxButtons.OK,
                    result.Kind == "need-manual" ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
            }
        }

        /// <summary>
        /// 「更新cookies」：发短信拿一批新的免密 cookie。
        /// 服务端每完成一次完整登录就会重新下发 4 个 AUTOAUTH_*，有效期是「此刻 + 90 天」，
        /// 登录成功后 LoginRunner 会自动把它们存回配置。
        /// </summary>
        private void OnRefreshClick(object sender, EventArgs e)
        {
            OnSendCodeClick(sender, e);
        }

        private void OnLogoutClick(object sender, EventArgs e)
        {
            _btnLogout.Enabled = false;
            _btnLogout.Text = "下线中…";

            // 勾选了「同时关闭守护」才停：否则下线断开后它会在几秒内用 cookie 登回去
            bool stopGuard = _chkStopGuard == null || _chkStopGuard.Checked;
            if (stopGuard)
            {
                try { Guard.Stop(); } catch (Exception ex) { AppLog.WriteErrorLog("停止守护失败: " + ex.Message); }
                AppLog.WriteErrorLog("下线：已先停止守护进程");
            }
            else
            {
                AppLog.WriteErrorLog("下线：按用户选择保留守护进程");
            }

            LoginSettings settings;
            try { settings = Config.Load().GetLoginSettings(); }
            catch (Exception ex)
            {
                _btnLogout.Enabled = true;
                _btnLogout.Text = "下线";
                MessageBox.Show(ex.Message, "下线结果", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var thread = new System.Threading.Thread(() =>
            {
                LoginResult result;
                try { result = LoginRunner.Logout(settings); }
                catch (Exception ex) { result = LoginResult.Fail("error", "下线异常：" + ex.Message); }

                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        _btnLogout.Enabled = true;
                        _btnLogout.Text = "下线";
                        AppLog.WriteErrorLog("下线结果：" + result.Message);
                        if (result.Ok) Status.Write("offline", result.Message, "logout", 60);
                        RefreshUi();
                    }));
                }
                catch (Exception ex) { AppLog.WriteErrorLog("回传下线结果失败: " + ex.Message); }
            });
            thread.IsBackground = true;
            thread.Start();
        }

        private void OnOpenLogClick(object sender, EventArgs e)
        {
            try
            {
                if (File.Exists(AppEnv.LogPath)) Process.Start("notepad.exe", "\"" + AppEnv.LogPath + "\"");
                else MessageBox.Show("还没有日志，守护还没运行过。", "CMCC-EDU 网络守护");
            }
            catch (Exception ex) { AppLog.WriteErrorLog("打开日志失败: " + ex.Message); }
        }

        private void OnClearLogClick(object sender, EventArgs e)
        {
            try
            {
                if (File.Exists(AppEnv.LogPath)) File.WriteAllText(AppEnv.LogPath, "");
                _logOffset = 0;
                _lastLogLen = -1;
                _txtLog.Text = "";
            }
            catch (Exception ex) { AppLog.WriteErrorLog("清空日志失败: " + ex.Message); }
        }

        private void CreateShortcut()
        {
            try
            {
                string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                string lnk = Path.Combine(desktop, "CMCC-EDU 网络守护.lnk");
                Type t = Type.GetTypeFromProgID("WScript.Shell");
                dynamic sh = Activator.CreateInstance(t);
                dynamic link = sh.CreateShortcut(lnk);
                link.TargetPath = Application.ExecutablePath;
                link.WorkingDirectory = AppEnv.Root;
                link.IconLocation = Path.Combine(AppEnv.Root, "assets", "app.ico") + ",0";
                link.Description = "CMCC-EDU 网络守护";
                link.Save();
                MessageBox.Show("已在桌面创建快捷方式：\r\n" + lnk, "CMCC-EDU 网络守护", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("创建失败：" + ex.Message, "CMCC-EDU 网络守护", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

    }
}
