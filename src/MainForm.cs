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
    internal sealed class MainForm : Form
    {
        private readonly EventWaitHandle _showEvent;

        // 配色
        private static readonly Color CText  = Color.FromArgb(31, 35, 40);
        private static readonly Color CMuted = Color.FromArgb(107, 114, 128);
        private static readonly Color CLine  = Color.FromArgb(232, 235, 239);
        private static readonly Color COk    = Color.FromArgb(5, 150, 105);
        private static readonly Color CWarn  = Color.FromArgb(217, 119, 6);
        private static readonly Color CBad   = Color.FromArgb(220, 38, 38);
        private static readonly Color CLogBg = Color.FromArgb(255, 255, 255);
        private static readonly Color CCard  = Color.FromArgb(243, 245, 248);
        private static readonly Color CCardEdge = Color.FromArgb(232, 235, 239);

        // 四块分区（对应：顶部控制 / 表单 / 日志 / 底部设置）
        private static readonly Rectangle[] Cards = new Rectangle[]
        {
            new Rectangle(8,   6, 504, 208),
            new Rectangle(8, 222, 504, 116),
            new Rectangle(8, 346, 504, 232),
            new Rectangle(8, 582, 504,  36)
        };

        // 控件
        private DotLabel _lblState;
        private Label _lblNet, _lblTime, _lblResult, _lblUser, _lblMode, _lblPass, _lblInterval, _lblLog;
        private UiButton _btnToggle, _btnCheck, _btnLogout, _btnRefresh, _btnSendCode, _btnSave, _btnStaticLogin, _btnLog, _btnClearLog;
        private UiCheckBox _chkStopGuard;
        private TextBox _txtUser, _txtPass, _txtLog;
        private RoundedField _fieldPass;   // 密码/验证码框的外框，需要跟着显隐
        private ThinScrollBar _logScroll; // 日志框的自制滚动条
        private DropDownField _cmbMode, _cmbInterval, _cmbGuiMode;
        private UiCheckBox _chkAutoLogin, _chkShowPwd, _chkAutoGuard, _chkAutoGui;
        private NotifyIcon _tray;
        private System.Windows.Forms.Timer _uiTimer;

        private long _logOffset;
        private long _lastLogLen = -1;
        private string _loginMode = "dynamic";
        private bool _suppressModeEvent;
        private bool _suppressAutoEvent;   // 初始化控件时不要触发"保存设置"
        private bool _loginBusy;
        private bool _reallyExit;
        private bool _quietStart;
        private Color _lastIconDot = Color.Empty;

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
            // 控件坐标是按 520x624 设计的，这里锁死客户区，
            // 否则窗口默认尺寸会让右侧多出空白、卡片左右不对称。
            ClientSize = new Size(520, 624);
            BackColor = Color.White;
            Font = new Font("微软雅黑", 9f);
            ForeColor = CText;

            _lblState = new DotLabel { Location = new Point(24, 14), Size = new Size(472, 34),
                Font = new Font("微软雅黑", 14f, FontStyle.Bold), Text = "守护已停止", ForeColor = CMuted,
                DotSize = 11, DotGap = 10 };
            Controls.Add(_lblState);

            _btnToggle    = MakeButton("开启守护", 24, 62, 148, 38, UiButtonKind.Primary);
            _btnCheck     = MakeButton("cookies登录", 182, 62, 148, 38, UiButtonKind.Secondary);
            _btnLogout    = MakeButton("下线", 340, 62, 156, 32, UiButtonKind.Danger);

            // 下线时是否连带关闭守护：默认勾选（和旧行为一致，避免下线后几秒被登回去）
            _chkStopGuard = new UiCheckBox
            {
                Location = new Point(340, 97),
                Size = new Size(160, 18),
                Text = "同时关闭守护",
                Checked = true,
                ForeColor = CMuted,
                Font = new Font("微软雅黑", 9f)
            };
            Controls.Add(_chkStopGuard);


            _lblNet = new Label { Location = new Point(24, 124), Size = new Size(356, 24),
                Text = "网络状态：—", ForeColor = CMuted };
            Controls.Add(_lblNet);

            _btnRefresh = MakeButton("更新cookies", 388, 122, 108, 26, UiButtonKind.Secondary);
            _btnRefresh.Click += OnRefreshClick;

            _lblTime = new Label { Location = new Point(24, 150), Size = new Size(472, 22),
                Text = "上次检查：—", ForeColor = CMuted };
            Controls.Add(_lblTime);

            _lblResult = new Label { Location = new Point(24, 174), Size = new Size(472, 40),
                Text = "最近结果：—", ForeColor = CMuted };
            Controls.Add(_lblResult);


            _lblUser = MakeFieldLabel("账号", 24, 237);
            _txtUser = new TextBox { Location = new Point(84, 234), Size = new Size(200, 28) };
            WrapField(_txtUser, 84, 234, 200, 30);

            _lblMode = MakeFieldLabel("方式", 300, 237, 44);
            _cmbMode = new DropDownField { Location = new Point(348, 234), Size = new Size(148, 28), SurfaceColor = CCard };
            _cmbMode.AddRange(new object[] { "静态密码", "动态密码（短信）" });
            _cmbMode.SelectedIndexChanged += OnModeChanged;
            Controls.Add(_cmbMode);

            _lblPass = MakeFieldLabel("密码", 24, 275);
            _txtPass = new TextBox { Location = new Point(84, 272), Size = new Size(150, 28), UseSystemPasswordChar = true };
            _fieldPass = WrapField(_txtPass, 84, 272, 150, 30);

            _chkShowPwd = new UiCheckBox { Location = new Point(240, 274), Size = new Size(56, 24), Text = "显示" };
            Controls.Add(_chkShowPwd);
            _chkShowPwd.CheckedChanged += (s, e) => _txtPass.UseSystemPasswordChar = !_chkShowPwd.Checked;

            _btnStaticLogin = MakeButton("静态密码登录", 300, 271, 96, 28, UiButtonKind.Secondary);
            _btnStaticLogin.Visible = false;
            _btnStaticLogin.Click += OnStaticLoginClick;

            _btnSendCode = MakeButton("发送验证码并登录", 84, 271, 150, 28, UiButtonKind.Primary);
            _btnSendCode.Click += OnSendCodeClick;

            _btnSave = MakeButton("保存账号", 396, 271, 100, 28, UiButtonKind.Secondary);
            _btnSave.Click += OnSaveClick;

            _lblInterval = MakeFieldLabel("检测间隔", 24, 310, 80);
            _cmbInterval = new DropDownField { Location = new Point(108, 307), Size = new Size(120, 28), SurfaceColor = CCard };
            _cmbInterval.AddRange(new object[] { "5 秒", "10 秒", "15 秒", "30 秒", "60 秒", "2 分钟", "5 分钟" });
            _cmbInterval.SelectedIndexChanged += OnIntervalChanged;
            Controls.Add(_cmbInterval);

            _chkAutoLogin = new UiCheckBox { Location = new Point(300, 310), Size = new Size(172, 24), Text = "下次自动登录", Checked = true };
            Controls.Add(_chkAutoLogin);


            _lblLog = new Label { Location = new Point(24, 358), Size = new Size(180, 22), Text = "最近日志", ForeColor = CMuted };
            Controls.Add(_lblLog);

            _btnLog      = MakeButton("打开日志", 312, 354, 84, 26, UiButtonKind.Secondary);
            _btnLog.Font = new Font("微软雅黑", 8f);
            _btnLog.Click += OnOpenLogClick;

            _btnClearLog = MakeButton("清空日志", 404, 354, 92, 26, UiButtonKind.Secondary);
            _btnClearLog.Font = new Font("微软雅黑", 8f);
            _btnClearLog.Click += OnClearLogClick;

            _txtLog = new TextBox
            {
                Location = new Point(24, 384), Size = new Size(472, 190),
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.None,
                WordWrap = true, BorderStyle = BorderStyle.FixedSingle,
                BackColor = CLogBg, ForeColor = CText, Font = new Font("微软雅黑", 9f)
            };
            // 日志框也套一层圆角外框，去掉系统默认的 3D 凹陷感
            var logField = new RoundedField
            {
                Location = new Point(24, 384),
                Size = new Size(472, 190),
                FillColor = Color.White,
                SurfaceColor = CCard
            };
            _txtLog.BorderStyle = BorderStyle.None;
            _txtLog.Location = new Point(8, 6);
            _txtLog.Size = new Size(446, 178);   // 右边留 10px 给自制滚动条
            logField.Controls.Add(_txtLog);

            // 自制细滚动条：原生那条 17px 宽、灰色，和圆角风格不搭
            _logScroll = new ThinScrollBar(_txtLog)
            {
                Location = new Point(458, 6),
                Size = new Size(8, 178),
                BackColor = Color.White
            };
            logField.Controls.Add(_logScroll);

            Controls.Add(logField);

            _chkAutoGuard = new UiCheckBox { Location = new Point(24, 586), Size = new Size(148, 26), Text = "开机启动守护" };
            _chkAutoGui   = new UiCheckBox { Location = new Point(180, 586), Size = new Size(148, 26), Text = "开机启动面板" };
            _cmbGuiMode   = new DropDownField { Location = new Point(340, 586), Size = new Size(116, 26), SurfaceColor = CCard };
            _cmbGuiMode.AddRange(new object[] { "托盘", "任务栏" });
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

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            // 画卡片背景。WinForms 的 Label 透明背景取的是窗体底色而不是卡片色，
            // 所以卡片用浅灰、窗体用白，标签只需染成 CCard 就能和卡片融为一体。
            var g = e.Graphics;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;

            foreach (var r in Cards)
            {
                using (var path = UiButton.RoundedPath(new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1), 10))
                using (var brush = new SolidBrush(CCard))
                using (var pen = new Pen(CCardEdge, 1f))
                {
                    g.FillPath(brush, path);
                    g.DrawPath(pen, path);
                }
            }
        }

        private UiButton MakeButton(string text, int x, int y, int w, int h, UiButtonKind kind = UiButtonKind.Secondary)
        {
            var b = new UiButton
            {
                Text = text,
                Location = new Point(x, y),
                Size = new Size(w, h),
                Kind = kind,
                Font = new Font("微软雅黑", h >= 34 ? 10f : 9f)
            };
            Controls.Add(b);
            return b;
        }

        private static Control MakeDivider(int x, int y, int w)
        {
            return new Panel { Location = new Point(x, y), Size = new Size(w, 1), BackColor = CLine };
        }

        /// <summary>
        /// 把输入控件塞进圆角外框里。外框负责画边框，控件本身去掉系统边框。
        /// 内边距左右 9px，垂直居中。
        /// </summary>
        private RoundedField WrapField(Control inner, int x, int y, int w, int h, int padX = 9, int padY = 0)
        {
            Controls.Remove(inner);

            var field = new RoundedField
            {
                Location = new Point(x, y),
                Size = new Size(w, h),
                FillColor = Color.White,
                SurfaceColor = CCard
            };

            // BorderStyle / PreferredHeight 是 TextBox 才有的，Control 上没有
            var asText = inner as TextBox;
            if (asText != null) asText.BorderStyle = BorderStyle.None;
            inner.BackColor = Color.White;

            int innerH = asText != null ? asText.PreferredHeight : inner.Height;
            if (innerH <= 0) innerH = inner.Height;
            int top = padY > 0 ? padY : Math.Max(0, (h - innerH) / 2);
            inner.Location = new Point(padX, top);
            inner.Width = Math.Max(10, w - padX * 2);

            field.Controls.Add(inner);

            // 外框必须跟着内部控件一起显隐，否则隐藏的输入框会留下一个空壳，
            // 把同一位置的按钮盖住（动态模式下「验证码」框会盖掉「发送验证码并登录」）。
            inner.VisibleChanged += delegate { field.Visible = inner.Visible; };
            field.Visible = inner.Visible;

            // 焦点时边框变蓝
            inner.GotFocus += delegate { field.SetFocused(true); };
            inner.LostFocus += delegate { field.SetFocused(false); };

            Controls.Add(field);
            return field;
        }

        private Label MakeFieldLabel(string text, int x, int y, int w = 52)
        {
            var label = new Label { Location = new Point(x, y), Size = new Size(w, 24), Text = text, ForeColor = CText };
            Controls.Add(label);
            return label;
        }

        // ---------------------------------------------------------------- 启动



        /// <summary>
        /// 把标签和复选框的底色染成卡片色。
        /// WinForms 的 Label 透明背景取的是窗体底色（白），直接放在灰卡片上会露出白块。
        /// </summary>
        private void PaintCardBackcolors()
        {
            foreach (Control ctl in Controls)
            {
                if (ctl is Label || ctl is CheckBox) ctl.BackColor = CCard;
            }
        }

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

        private void InitTray()
        {
            _tray = new NotifyIcon
            {
                Icon = IconFactory.CreateIcon(16, IconFactory.DotIdle),
                Text = "CMCC-EDU 网络守护",
                Visible = true
            };

            // 托盘菜单也用分层窗口菜单：圆角真正抗锯齿，和下拉菜单一致

            var openEntry = new MenuEntry { Text = "打开面板", Click = delegate { ShowPanel(); } };
            _menuToggle = new MenuEntry
            {
                Text = Guard.IsRunning() ? "关闭守护" : "开启守护",
                Click = delegate
                {
                    if (Guard.IsRunning()) Guard.Stop(); else Guard.Start();
                    RefreshUi();
                }
            };
            var shortcutEntry = new MenuEntry { Text = "创建桌面快捷方式", Click = delegate { CreateShortcut(); } };
            var exitEntry = new MenuEntry { Text = "退出面板", Click = delegate { OnTrayExit(); } };

            _menuEntries.Add(openEntry);
            _menuEntries.Add(_menuToggle);
            _menuEntries.Add(shortcutEntry);
            _menuEntries.Add(MenuEntry.Sep());
            _menuEntries.Add(exitEntry);
            _tray.MouseDoubleClick += (s, e) => ShowPanel();
            _tray.MouseClick += (s, e) =>
            {
                if (e.Button == MouseButtons.Left) ShowPanel();
                else if (e.Button == MouseButtons.Right) ShowTrayMenu();
            };

            AppLog.WriteErrorLog("托盘图标已创建  Visible=True");
        }

        private readonly List<MenuEntry> _menuEntries = new List<MenuEntry>();
        private MenuEntry _menuToggle;

        /// <summary>右键托盘图标时弹出菜单。</summary>
        private void ShowTrayMenu()
        {
            try
            {
                var menu = new LayeredMenu(_menuEntries);
                menu.ShowAt(Cursor.Position);
            }
            catch (Exception ex) { AppLog.WriteErrorLog("弹出托盘菜单失败: " + ex.Message); }
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
            // 窗体还没显示时 Visible 恒为 false，改它不触发 VisibleChanged，
            // 所以外框必须显式同步，否则会留下空壳盖住「发送验证码并登录」。
            if (_fieldPass != null) _fieldPass.Visible = !dynamic;
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

        // ---------------------------------------------------------------- 刷新

        private void RefreshUi()
        {
            bool running = Guard.IsRunning();
            var st = Status.Read();

            _lblState.Text = running ? "守护运行中" : "守护已停止";
            _lblState.DotColor = running ? COk : Color.FromArgb(156, 163, 175);
            _lblState.ForeColor = running ? COk : CMuted;
            _btnToggle.Text = running ? "关闭守护" : "开启守护";
            if (_menuToggle != null) _menuToggle.Text = running ? "关闭守护" : "开启守护";

            bool busy = st != null && (st.State == "busy" || st.State == "captcha");
            bool online = st != null && st.State == "online";

            // 手动检测结果：状态文件没给结论时用它兜底
            if (st != null && !string.IsNullOrEmpty(st.State) && st.State != "error")
            {
                _lblTime.Text = "上次检查：" + st.Time + CookieHint();
                _lblResult.Text = "最近结果：" + st.Message;
                ApplyStateText(st.State);
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

        /// <summary>免密 cookie 的剩余有效期，显示在网络状态后面。</summary>
        private string CookieHint()
        {
            try
            {
                string raw = Config.Load().GetString("autoCookiesExpire");
                if (string.IsNullOrWhiteSpace(raw)) return "";
                DateTime dt;
                if (!DateTime.TryParse(raw, out dt)) return "";

                int days = (int)Math.Floor((dt - DateTime.Now).TotalDays);
                if (days < 0) return "    ·  cookie 已过期（" + dt.ToString("yyyy-MM-dd") + "）";
                if (days == 0) return "    ·  cookie 今天到期";
                return "    ·  cookie 至 " + dt.ToString("yyyy-MM-dd") + "（剩 " + days + " 天）";
            }
            catch { return ""; }
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
