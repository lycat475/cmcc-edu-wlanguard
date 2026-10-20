using System;

namespace CmccEdu
{
    /// <summary>命令行一次性任务：-force 登录、-logout 下线、-diagnose 诊断。</summary>
    internal static class Cli
    {
        public static int RunLogin()
        {
            LoginSettings s;
            try { s = Config.Load().GetLoginSettings(); }
            catch (Exception ex)
            {
                AppLog.Write("配置有误: " + ex.Message, "CFG");
                return 2;
            }

            string mode = AppEnv.SmsLogin ? "sms"
                        : AppEnv.PasswordLogin ? "password"
                        : AppEnv.CookieOnly ? "cookie"
                        : "auto";

            // 命令行没有 UI，需要验证码时直接失败
            var r = LoginRunner.ManualLogin(s, mode, null);
            if (r.Ok)
            {
                AppLog.Write(r.Message, "AUTH");
                Status.Write("online", r.Message, "login_ok", s.Interval);
                return 0;
            }

            AppLog.Write(r.Message, "AUTH");
            Status.Write("offline", r.Message, "login_fail", s.Interval);
            return 1;
        }

        public static int RunLogout()
        {
            LoginSettings s;
            try { s = Config.Load().GetLoginSettings(); }
            catch (Exception ex)
            {
                AppLog.Write("配置有误: " + ex.Message, "CFG");
                return 2;
            }

            var r = LoginRunner.Logout(s);
            AppLog.Write(r.Message, "LOGOUT");
            return r.Ok ? 0 : 1;
        }

        public static int RunDiagnose()
        {
            Console.WriteLine("CMCC-EDU 网络守护 诊断");
            Console.WriteLine("程序目录: " + AppEnv.Root);

            try
            {
                var s = Config.Load().GetLoginSettings();
                Console.WriteLine("手机号: " + Mask(s.Username));
                Console.WriteLine("登录方式: " + s.LoginMode);
                Console.WriteLine("Portal: " + s.Portal);
                Console.WriteLine("SSID: " + s.Ssid);
                Console.WriteLine("免密 cookie: " + (string.IsNullOrWhiteSpace(s.AutoCookies) ? "未配置" : "已配置"));
                Console.WriteLine("检测间隔: " + s.Interval + " 秒");
            }
            catch (Exception ex)
            {
                Console.WriteLine("配置: " + ex.Message);
            }

            Console.WriteLine("在线状态: " + (Network.IsOnline() ? "已联网" : "未联网"));
            Console.WriteLine("本机 IP: " + (Network.GetDefaultRouteIp() ?? "拿不到"));
            return 0;
        }

        private static string Mask(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length < 7) return s;
            return s.Substring(0, 3) + "****" + s.Substring(s.Length - 4);
        }
    }
}