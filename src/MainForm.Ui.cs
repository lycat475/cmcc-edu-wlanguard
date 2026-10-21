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
    /// <summary> 界面构建与绘制、托盘、状态显示。</summary>
    internal sealed partial class MainForm
    {
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

        private void RestoreLoginButtons()
        {
            _loginBusy = false;
            _btnCheck.Text = "cookies登录";
            _btnSendCode.Text = "发送验证码并登录";
            _btnStaticLogin.Text = "静态密码登录";
            SafeDelete.Try(delegate { RefreshUi(); }, "刷新界面", "UI");
        }

        private void SetLoginButtons(bool enabled)
        {
            _btnCheck.Enabled = enabled;
            _btnSendCode.Enabled = enabled;
            _btnStaticLogin.Enabled = enabled;
        }

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
                SafeDelete.Try(delegate { _tray.Icon = IconFactory.CreateIcon(16, dot); }, "设置托盘图标", "UI");
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

    }
}
