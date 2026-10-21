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
    internal sealed partial class MainForm : Form
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

        // ---------------------------------------------------------------- 启动

        private readonly List<MenuEntry> _menuEntries = new List<MenuEntry>();
        private MenuEntry _menuToggle;

        // ---------------------------------------------------------------- 配置

        // ---------------------------------------------------------------- 操作

        // ---------------------------------------------------------------- 刷新

        /// <summary>面板里不显示 [AUTH] [SMS] 这类分类标签，日志文件里保留。</summary>
        private static string StripTag(string line)
        {
            return System.Text.RegularExpressions.Regex.Replace(line, @"\[[A-Z]{2,10}\]\s*", "");
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                // 退出阶段：销毁失败也无所谓，进程马上就结束了，没必要记日志
                if (_tray != null) { try { _tray.Visible = false; _tray.Dispose(); } catch { } }
                if (_uiTimer != null) { try { _uiTimer.Dispose(); } catch { } }
                IconFactory.ReleaseAll();   // 释放缓存里的 HICON
            }
            base.Dispose(disposing);
        }
    }
}
