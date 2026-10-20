using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace CmccEdu
{
    internal sealed class MainForm : Form
    {
        private readonly EventWaitHandle _showEvent;

        // 配色
        private static readonly Color CText  = Color.FromArgb(32, 32, 32);
        private static readonly Color CMuted = Color.FromArgb(122, 122, 122);
        private static readonly Color CLine  = Color.FromArgb(228, 228, 228);
        private static readonly Color COk    = Color.FromArgb(10, 140, 70);
        private static readonly Color CWarn  = Color.FromArgb(190, 90, 0);
        private static readonly Color CBad   = Color.FromArgb(190, 40, 40);
        private static readonly Color CLogBg = Color.FromArgb(247, 248, 250);

        // 控件
        private Label _lblState, _lblNet, _lblTime, _lblResult, _lblUser, _lblMode, _lblPass, _lblInterval, _lblLog;
        private Button _btnToggle, _btnCheck, _btnLogout, _btnRefresh, _btnSendCode, _btnSave, _btnStaticLogin, _btnLog, _btnClearLog;
        private TextBox _txtUser, _txtPass, _txtLog;
        private ComboBox _cmbMode, _cmbInterval, _cmbGuiMode;
        private CheckBox _chkAutoLogin, _chkShowPwd, _chkAutoGuard, _chkAutoGui;
        private NotifyIcon _tray;
        private System.Windows.Forms.Timer _uiTimer;

        private long _logOffset;
        private long _lastLogLen = -1;
        private string _loginMode = "dynamic";
        private bool _suppressModeEvent;
        private bool _suppressAutoEvent;   // 初始化控件时不要触发"保存设置"
        private bool _loginBusy;
        private bool _refreshBusy;
        private bool _reallyExit;
        private bool _quietStart;
        private Color _lastIconDot = Color.Empty;
        private Color? _manualDot;
        private string _manualText;
        private DateTime _manualAt;

        public MainForm(EventWaitHandle showEvent)
        {
            _showEvent = showEvent;
            BuildUi();
            // 窗体图标必须显式设置，否则标题栏显示的是 WinForms 默认图标
            try { Icon = IconFactory.CreateIcon(32, IconFactory.DotIdle); }
            catch (Exception ex) { AppLog.WriteErrorLog("设置窗口图标失败: " + ex.Message); }
            LoadConfigIntoUi();
        }

        // ---------------------------------------------------------------- 界面

        private void BuildUi()
        {
            Text = "CMCC-EDU 网络守护";
            Size = new Size(536, 660);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            BackColor = Color.White;
            Font = new Font("微软雅黑", 9f);
            ForeColor = CText;

            _lblState = new Label { Location = new Point(24, 14), Size = new Size(472, 34),
                Font = new Font("微软雅黑", 15f, FontStyle.Bold), Text = "●  守护已停止", ForeColor = CMuted };
            Controls.Add(_lblState);

            _btnToggle    = MakeButton("开启守护", 24, 62, 148, 38);
            _btnCheck     = MakeButton("cookies登录", 182, 62, 148, 38);
            _btnLogout    = MakeButton("下线（关闭守护）", 340, 62, 156, 38);

            Controls.Add(MakeDivider(24, 112, 472));

            _lblNet = new Label { Location = new Point(24, 124), Size = new Size(372, 24),
                Text = "网络状态：—", ForeColor = CMuted };
            Controls.Add(_lblNet);

            _btnRefresh = MakeButton("更新", 408, 122, 88, 26);
            _btnRefresh.Click += OnRefreshClick;

            _lblTime = new Label { Location = new Point(24, 150), Size = new Size(472, 22),
                Text = "上次检查：—", ForeColor = CMuted };
            Controls.Add(_lblTime);

            _lblResult = new Label { Location = new Point(24, 174), Size = new Size(472, 40),
                Text = "最近结果：—", ForeColor = CMuted };
            Controls.Add(_lblResult);

            Controls.Add(MakeDivider(24, 222, 472));

            _lblUser = MakeFieldLabel("账号", 24, 237);
            _txtUser = new TextBox { Location = new Point(84, 234), Size = new Size(200, 28) };
            Controls.Add(_txtUser);

            _lblMode = MakeFieldLabel("方式", 300, 237, 44);
            _cmbMode = new ComboBox { Location = new Point(348, 234), Size = new Size(148, 28), DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbMode.Items.AddRange(new object[] { "静态密码", "动态密码（短信）" });
            _cmbMode.SelectedIndexChanged += OnModeChanged;
            Controls.Add(_cmbMode);

            _lblPass = MakeFieldLabel("密码", 24, 275);
            _txtPass = new TextBox { Location = new Point(84, 272), Size = new Size(150, 28), UseSystemPasswordChar = true };
            Controls.Add(_txtPass);

            _chkShowPwd = new CheckBox { Location = new Point(240, 274), Size = new Size(56, 24), Text = "显示" };
            Controls.Add(_chkShowPwd);
            _chkShowPwd.CheckedChanged += (s, e) => _txtPass.UseSystemPasswordChar = !_chkShowPwd.Checked;

            _btnStaticLogin = MakeButton("静态密码登录", 300, 271, 96, 28);
            _btnStaticLogin.Visible = false;
            _btnStaticLogin.Click += OnStaticLoginClick;

            _btnSendCode = MakeButton("发送验证码并登录", 84, 271, 150, 28);
            _btnSendCode.Click += OnSendCodeClick;

            _btnSave = MakeButton("保存账号", 396, 271, 100, 28);
            _btnSave.Click += OnSaveClick;

            _lblInterval = MakeFieldLabel("检测间隔", 24, 310, 80);
            _cmbInterval = new ComboBox { Location = new Point(108, 307), Size = new Size(120, 28), DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbInterval.Items.AddRange(new object[] { "5 秒", "10 秒", "15 秒", "30 秒", "60 秒", "2 分钟", "5 分钟" });
            _cmbInterval.SelectedIndexChanged += OnIntervalChanged;
            Controls.Add(_cmbInterval);

            _chkAutoLogin = new CheckBox { Location = new Point(300, 310), Size = new Size(172, 24), Text = "下次自动登录", Checked = true };
            Controls.Add(_chkAutoLogin);

            Controls.Add(MakeDivider(24, 346, 472));

            _lblLog = new Label { Location = new Point(24, 358), Size = new Size(180, 22), Text = "最近日志", ForeColor = CMuted };
            Controls.Add(_lblLog);

            _btnLog      = MakeButton("打开日志", 312, 354, 84, 26);
            _btnLog.Font = new Font("微软雅黑", 8f);
            _btnLog.Click += OnOpenLogClick;

            _btnClearLog = MakeButton("清空日志", 404, 354, 92, 26);
            _btnClearLog.Font = new Font("微软雅黑", 8f);
            _btnClearLog.Click += OnClearLogClick;

            _txtLog = new TextBox
            {
                Location = new Point(24, 384), Size = new Size(472, 190),
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                WordWrap = true, BorderStyle = BorderStyle.FixedSingle,
                BackColor = CLogBg, ForeColor = CText, Font = new Font("微软雅黑", 9f)
            };
            Controls.Add(_txtLog);

            _chkAutoGuard = new CheckBox { Location = new Point(24, 586), Size = new Size(148, 26), Text = "开机启动守护" };
            _chkAutoGui   = new CheckBox { Location = new Point(180, 586), Size = new Size(148, 26), Text = "开机启动面板" };
            _cmbGuiMode   = new ComboBox { Location = new Point(340, 583), Size = new Size(116, 28), DropDownStyle = ComboBoxStyle.DropDownList };
            _cmbGuiMode.Items.AddRange(new object[] { "托盘", "任务栏" });
            _cmbGuiMode.SelectedIndex = 0;
            Controls.Add(_chkAutoGuard);
            Controls.Add(_chkAutoGui);
            Controls.Add(_cmbGuiMode);

            // 事件
            _btnToggle.Click += OnToggleGuardClick;
            _btnCheck.Click += OnCookieLoginClick;
            _btnLogout.Click += OnLogoutClick;

            _chkAutoGuard.CheckedChanged += OnAutoGuardChanged;
            _chkAutoGui.CheckedChanged += OnAutoGuiChanged;
            _cmbGuiMode.SelectedIndexChanged += OnGuiModeChanged;

            FormClosing += OnFormClosing;
            Shown += OnShown;
        }

        private Button MakeButton(string text, int x, int y, int w, int h)
        {
            var b = new Button { Text = text, Location = new Point(x, y), Size = new Size(w, h) };
            Controls.Add(b);
            return b;
        }

        private static Control MakeDivider(int x, int y, int w)
        {
            return new Panel { Location = new Point(x, y), Size = new Size(w, 1), BackColor = CLine };
        }

        private Label MakeFieldLabel(string text, int x, int y, int w = 52)
        {
            var label = new Label { Location = new Point(x, y), Size = new Size(w, 24), Text = text, ForeColor = CText };
            Controls.Add(label);
            return label;
        }

        // ---------------------------------------------------------------- 启动

        private void OnShown(object sender, EventArgs e)
        {
            AppLog.WriteErrorLog("面板已显示");

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

        private void InitTray()
        {
            _tray = new NotifyIcon
            {
                Icon = IconFactory.CreateIcon(16, IconFactory.DotIdle),
                Text = "CMCC-EDU 网络守护",
                Visible = true
            };

            var menu = new ContextMenuStrip();
            menu.Items.Add("打开面板", null, (s, e) => ShowPanel());

            var toggle = menu.Items.Add("关闭守护", null, (s, e) =>
            {
                if (Guard.IsRunning()) Guard.Stop(); else Guard.Start();
                RefreshUi();
            });
            _menuToggle = toggle;

            menu.Items.Add("创建桌面快捷方式", null, (s, e) => CreateShortcut());
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("退出面板", null, (s, e) => OnTrayExit());
            _tray.ContextMenuStrip = menu;

            _tray.MouseDoubleClick += (s, e) => ShowPanel();
            _tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ShowPanel(); };

            AppLog.WriteErrorLog("托盘图标已创建  Visible=True");
        }

        private ToolStripItem _menuToggle;

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
                try { Guard.Stop(); } catch { }
                AppLog.WriteErrorLog("退出面板并关闭守护");
            }
            else AppLog.WriteErrorLog("退出面板，守护继续运行");

            ExitPanel();
        }

        private void ExitPanel()
        {
            _reallyExit = true;
            try { if (_uiTimer != null) _uiTimer.Stop(); } catch { }
            try { if (_tray != null) { _tray.Visible = false; _tray.Dispose(); } } catch { }
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

        // ---------------------------------------------------------------- 配置

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
            _cmbGuiMode.SelectedIndex = string.Equals(cfg.GetString("guiStartMode"), "taskbar", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
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

        private void ApplyLoginMode()
        {
            bool dynamic = _loginMode == "dynamic";
            _txtPass.Visible = !dynamic;
            _chkShowPwd.Visible = !dynamic;
            _lblPass.Text = dynamic ? "验证码" : "密码";
            _btnSendCode.Visible = dynamic;
            _btnStaticLogin.Visible = !dynamic;
            if (dynamic) _txtPass.Text = "";
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

        // ---------------------------------------------------------------- 操作

        private void OnToggleGuardClick(object sender, EventArgs e)
        {
            try
            {
                if (Guard.IsRunning()) Guard.Stop(); else Guard.Start();
            }
            catch (Exception ex) { AppLog.WriteErrorLog("切换守护失败: " + ex.Message); }
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

        private void RestoreLoginButtons()
        {
            _loginBusy = false;
            _btnCheck.Text = "cookies登录";
            _btnSendCode.Text = "发送验证码并登录";
            _btnStaticLogin.Text = "静态密码登录";
            try { RefreshUi(); } catch { }
        }

        private void SetLoginButtons(bool enabled)
        {
            _btnCheck.Enabled = enabled;
            _btnSendCode.Enabled = enabled;
            _btnStaticLogin.Enabled = enabled;
        }

        private void OnRefreshClick(object sender, EventArgs e)
        {
            if (_refreshBusy) return;
            _refreshBusy = true;
            _btnRefresh.Enabled = false;
            _btnRefresh.Text = "检测中…";

            // 探测最长要 3 秒，放后台跑，别冻住界面
            var thread = new System.Threading.Thread(() =>
            {
                bool online = Network.IsOnline(3);
                try
                {
                    BeginInvoke(new Action(() =>
                    {
                        _manualDot = online ? COk : CWarn;
                        _manualText = online ? "已联网" : "未联网";
                        _manualAt = DateTime.Now;
                        _btnRefresh.Text = "更新";
                        _btnRefresh.Enabled = true;
                        _refreshBusy = false;
                        RefreshUi();
                        AppLog.WriteErrorLog("手动更新状态：" + _manualText);
                    }));
                }
                catch (Exception ex)
                {
                    // 面板已经关了
                    _refreshBusy = false;
                    AppLog.WriteErrorLog("回传检测结果失败: " + ex.Message);
                }
            });
            thread.IsBackground = true;
            thread.Start();
        }

        private void OnLogoutClick(object sender, EventArgs e)
        {
            _btnLogout.Enabled = false;
            _btnLogout.Text = "下线中…";

            // 先停守护：否则下线断开后它会在几秒内用 cookie 登回去
            try { Guard.Stop(); } catch (Exception ex) { AppLog.WriteErrorLog("停止守护失败: " + ex.Message); }
            AppLog.WriteErrorLog("下线：已先停止守护进程");

            LoginSettings settings;
            try { settings = Config.Load().GetLoginSettings(); }
            catch (Exception ex)
            {
                _btnLogout.Enabled = true;
                _btnLogout.Text = "下线（关闭守护）";
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
                        _btnLogout.Text = "下线（关闭守护）";
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

        // ---------------------------------------------------------------- 刷新

        private void RefreshUi()
        {
            bool running = Guard.IsRunning();
            var st = Status.Read();

            _lblState.Text = running ? "●  守护运行中" : "●  守护已停止";
            _lblState.ForeColor = running ? COk : CMuted;
            _btnToggle.Text = running ? "关闭守护" : "开启守护";
            if (_menuToggle != null) _menuToggle.Text = running ? "关闭守护" : "开启守护";

            bool busy = st != null && (st.State == "busy" || st.State == "captcha");
            bool online = st != null && st.State == "online";

            // 手动检测结果：状态文件没给结论时用它兜底
            if (st != null && !string.IsNullOrEmpty(st.State) && st.State != "error")
            {
                _lblTime.Text = "上次检查：" + st.Time;
                _lblResult.Text = "最近结果：" + st.Message;
                ApplyStateText(st.State);
            }
            else if (_manualAt != default(DateTime) && (DateTime.Now - _manualAt).TotalMinutes <= 2)
            {
                _lblNet.Text = "网络状态：" + _manualText + "（手动检测 " + _manualAt.ToString("HH:mm:ss") + "）";
                _lblNet.ForeColor = _manualDot.Value;
                _lblTime.Text = "上次检查：" + _manualAt.ToString("HH:mm:ss") + "（手动）";
                _lblResult.Text = running ? "最近结果：--" : "最近结果：守护未运行，以上为手动检测结果";
            }
            else
            {
                _lblNet.Text = "网络状态：—";
                _lblNet.ForeColor = CMuted;
                _lblTime.Text = "上次检查：—";
                _lblResult.Text = "最近结果：—";
            }

            SetLoginButtons(!(_loginBusy || busy || online));

            // 已登录时跟随实际使用的登录方式
            if (st != null && st.State == "online" && !string.IsNullOrEmpty(st.Mode) && st.Mode != _loginMode)
            {
                SetLoginMode(st.Mode);
            }

            RefreshLogPanel();

            // 标题栏图标跟着状态走（只在状态变化时重建，避免频繁造图标）
            Color formDot = !running ? IconFactory.DotIdle : online ? IconFactory.DotOk : busy ? IconFactory.DotWarn : IconFactory.DotBad;
            if (_lastIconDot != formDot)
            {
                _lastIconDot = formDot;
                try { Icon = IconFactory.CreateIcon(32, formDot); } catch { /* 图标失败不影响功能 */ }
            }

            if (_tray != null)
            {
                Color dot = !running ? IconFactory.DotIdle : online ? IconFactory.DotOk : busy ? IconFactory.DotWarn : IconFactory.DotBad;
                try { _tray.Icon = IconFactory.CreateIcon(16, dot); } catch { }
                string tip = "CMCC-EDU 网络守护：" + (running ? (online ? "运行中·已联网" : "运行中·未联网") : "已停止");
                if (tip.Length > 63) tip = tip.Substring(0, 63);
                _tray.Text = tip;
            }
        }

        private void ApplyStateText(string state)
        {
            switch (state)
            {
                case "online":  _lblNet.Text = "网络状态：已联网"; _lblNet.ForeColor = COk; break;
                case "offline": _lblNet.Text = "网络状态：未联网"; _lblNet.ForeColor = CWarn; break;
                case "busy":    _lblNet.Text = "网络状态：认证进行中"; _lblNet.ForeColor = CWarn; break;
                case "captcha": _lblNet.Text = "网络状态：等待输入验证码"; _lblNet.ForeColor = CWarn; break;
                case "error":   _lblNet.Text = "网络状态：异常"; _lblNet.ForeColor = CBad; break;
                default:        _lblNet.Text = "网络状态：未知"; _lblNet.ForeColor = CMuted; break;
            }
        }

        private void RefreshLogPanel()
        {
            if (!File.Exists(AppEnv.LogPath))
            {
                _txtLog.Text = "（本次启动后暂无新日志；点「打开日志」可查看完整历史）";
                return;
            }

            long len;
            try { len = new FileInfo(AppEnv.LogPath).Length; }
            catch { return; }

            if (len == _lastLogLen) return;
            _lastLogLen = len;

            if (len < _logOffset) _logOffset = 0;   // 文件被清空过

            var lines = new System.Collections.Generic.List<string>();
            try
            {
                using (var fs = new FileStream(AppEnv.LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    fs.Seek(_logOffset, SeekOrigin.Begin);
                    using (var sr = new StreamReader(fs, Encoding.UTF8))
                    {
                        string line;
                        while ((line = sr.ReadLine()) != null)
                        {
                            line = StripTag(line);
                            if (line.Length > 0) lines.Add(line);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.WriteErrorLog("读取日志失败: " + ex.Message);
                return;
            }

            if (lines.Count > 40) lines = lines.GetRange(lines.Count - 40, 40);
            _txtLog.Text = lines.Count == 0
                ? "（本次启动后暂无新日志；点「打开日志」可查看完整历史）"
                : string.Join("\r\n", lines.ToArray());
            _txtLog.SelectionStart = _txtLog.TextLength;
            _txtLog.ScrollToCaret();
        }

        /// <summary>面板里不显示 [AUTH] [SMS] 这类分类标签，日志文件里保留。</summary>
        private static string StripTag(string line)
        {
            return System.Text.RegularExpressions.Regex.Replace(line, @"\[[A-Z]{2,10}\]\s*", "");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_tray != null) { try { _tray.Visible = false; _tray.Dispose(); } catch { } }
                if (_uiTimer != null) { try { _uiTimer.Dispose(); } catch { } }
                IconFactory.ReleaseAll();   // 释放缓存里的 HICON
            }
            base.Dispose(disposing);
        }
    }
}
