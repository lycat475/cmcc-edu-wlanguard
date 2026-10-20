using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace CmccEdu
{
    /// <summary>全局路径与启动参数。</summary>
    internal static class AppEnv
    {
        public static string Root;
        public static string ConfigPath;
        public static string LogPath;
        public static string StatusPath;
        public static string PidPath;
        public static string ErrorLogPath;

        // 命令行
        public static bool StartHidden;   // -tray
        public static bool Force;         // -force
        public static bool CookieOnly;    // -cookies
        public static bool SmsLogin;      // -sms
        public static bool PasswordLogin; // -password
        public static bool LoopMode;      // -loop
        public static bool LogoutMode;    // -logout
        public static bool Diagnose;      // -diagnose

        public static void Init()
        {
            Root = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\');
            ConfigPath  = Path.Combine(Root, "config.json");
            LogPath     = Path.Combine(Root, "autologin.log");
            StatusPath  = Path.Combine(Root, "status.json");
            PidPath     = Path.Combine(Root, "guard.pid");
            ErrorLogPath = Path.Combine(Root, "gui_error.log");
        }

        public static void ParseArgs(string[] args)
        {
            foreach (var a in args)
            {
                switch (a.ToLowerInvariant())
                {
                    case "-tray":     StartHidden = true;  break;
                    case "-force":    Force = true;        break;
                    case "-cookies":  CookieOnly = true;   break;
                    case "-sms":      SmsLogin = true;     break;
                    case "-password": PasswordLogin = true; break;
                    case "-loop":     LoopMode = true;     break;
                    case "-logout":   LogoutMode = true;   break;
                    case "-diagnose": Diagnose = true;     break;
                }
            }
        }
    }

    internal static class Program
    {
        private const string GuiMutexName  = @"Local\CmccEduGui";
        private const string LoopMutexName = @"Local\CmccEduGuardLoop";
        private const string ShowEventName = @"Local\CmccEduShow";

        [STAThread]
        private static int Main(string[] args)
        {
            AppEnv.Init();
            AppEnv.ParseArgs(args);

            try
            {
                if (AppEnv.LoopMode)     return Guard.Run();
                if (AppEnv.LogoutMode)   return Cli.RunLogout();
                if (AppEnv.Diagnose)     return Cli.RunDiagnose();
                if (AppEnv.Force || AppEnv.CookieOnly || AppEnv.SmsLogin || AppEnv.PasswordLogin)
                    return Cli.RunLogin();
            }
            catch (Exception ex)
            {
                AppLog.Write("异常: " + ex.Message, "ERR");
                return 2;
            }

            return RunGui();
        }

        private static int RunGui()
        {
            bool created;
            using (var mutex = new Mutex(true, GuiMutexName, out created))
            {
                if (!created)
                {
                    // 已有实例在跑：通知它把窗口显示出来，本进程退出。
                    // 不能静默退出，否则用户双击图标会觉得"没反应"。
                    try
                    {
                        using (var ev = EventWaitHandle.OpenExisting(ShowEventName))
                            ev.Set();
                    }
                    catch { /* 对方还没建事件：忽略，它很快就会自己显示 */ }

                    AppLog.Write("已有面板在运行，已请求它显示窗口", "SYS");
                    Thread.Sleep(400);
                    return 0;
                }

                using (var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName))
                {
                    System.Windows.Forms.Application.EnableVisualStyles();
                    System.Windows.Forms.Application.SetCompatibleTextRenderingDefault(false);
                    AppLog.Write("面板启动", "SYS");
                    using (var form = new MainForm(showEvent))
                        System.Windows.Forms.Application.Run(form);
                }
            }
            return 0;
        }
    }
}
