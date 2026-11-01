using System;
using System.Threading;
using Microsoft.Win32;

namespace CmccEdu
{
    /// <summary>
    /// 关机 / 重启前自动下线。
    ///
    /// 做法很简单：
    ///   收到会话结束事件（只处理关机/重启，注销不管）
    ///   → 在事件里同步做下线，最多等 3.5 秒
    ///   → 做完就返回，系统继续关机
    ///
    /// 时机说明：
    ///   用户点关机后，屏幕马上开始转圈，系统给每个程序约 5 秒（WaitToKillAppTimeout）
    ///   退出。这段时间进程还活着、网络也还在，正好用来发下线请求。
    ///
    /// 为什么不拦截关机再重发：
    ///   拦截需要知道这次是关机还是重启（会话结束事件不带这个信息，只能翻系统日志），
    ///   还要处理"拦截后没关成"的多种状态，反而可能把关机卡住。不拦截就没有这些麻烦。
    /// </summary>
    internal static class ShutdownLogout
    {
        private const string Key = "logoutOnShutdown";
        private const int MaxWaitMs = 3500;    // 下线总预算，剩下约 1.5 秒留给系统收尾

        /// <summary>挂上会话结束事件。窗口显示之后调用。</summary>
        public static void Attach()
        {
            try
            {
                SystemEvents.SessionEnding += OnSessionEnding;
                AppLog.Write("关机前自动下线：" + (IsEnabled() ? "已启用" : "已关闭"), "LOGOUT");
            }
            catch (Exception ex) { AppLog.WriteErrorLog("挂载关机事件失败: " + ex.Message); }
        }

        /// <summary>是否启用（配置里没有这一项时默认启用）。</summary>
        public static bool IsEnabled()
        {
            try
            {
                string v = Config.Load().GetString(Key);
                if (string.IsNullOrEmpty(v)) return true;
                return !string.Equals(v, "false", StringComparison.OrdinalIgnoreCase);
            }
            catch { return true; }
        }

        /// <summary>开关这个功能，立即写回配置。</summary>
        public static void SetEnabled(bool on)
        {
            try
            {
                var cfg = Config.Load();
                cfg.SetString(Key, on ? "true" : "false");
                cfg.Save();
                AppLog.Write("关机前自动下线：" + (on ? "已开启" : "已关闭"), "LOGOUT");
            }
            catch (Exception ex) { AppLog.WriteErrorLog("保存关机下线设置失败: " + ex.Message); }
        }

        private static void OnSessionEnding(object sender, SessionEndingEventArgs e)
        {
            if (e.Reason == SessionEndReasons.Logoff) return;   // 注销不处理

            if (!IsEnabled())
            {
                AppLog.Write("关机前自动下线已关闭，跳过", "LOGOUT");
                return;
            }

            AppLog.Write("检测到关机/重启，开始下线（最多等 " + MaxWaitMs + " 毫秒）", "LOGOUT");

            // 放到后台线程做，主线程 Join 等它；超时就放弃，绝不拖住关机
            var worker = new Thread(delegate() { TryLogout(); });
            worker.IsBackground = true;
            worker.Start();

            if (!worker.Join(MaxWaitMs))
            {
                AppLog.Write("下线超时（" + MaxWaitMs + " 毫秒内未完成），放行关机", "LOGOUT");
            }
        }

        /// <summary>停守护并调下线。失败只记日志，绝不抛。</summary>
        private static void TryLogout()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                // 先停守护，否则它会在下线后几秒又用 cookie 登回去
                Guard.Stop();
            }
            catch (Exception ex) { AppLog.WriteErrorLog("关机前停止守护失败: " + ex.Message); }

            try
            {
                var settings = Config.Load().GetLoginSettings();
                // 关机场景：每个请求最多 1.5 秒，下线后不等 3 秒复查（来不及时）
                var r = LoginRunner.Logout(settings, 0, 1500);
                AppLog.Write("关机前下线：" + r.Message + "（用时 " + sw.ElapsedMilliseconds + " 毫秒）", "LOGOUT");
            }
            catch (Exception ex)
            {
                AppLog.WriteErrorLog("关机前下线异常: " + ex.Message);
            }
        }
    }
}
