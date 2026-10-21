using System;
using System.Diagnostics;
using System.IO;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace CmccEdu
{
    /// <summary>守护进程的启停与主循环。</summary>
    internal static class Guard
    {
        private const string LoopMutexName = @"Local\CmccEduGuardLoop";

        // 网络状态一变就唤醒睡眠中的循环，不用干等一个完整间隔
        private static readonly ManualResetEventSlim WakeSignal = new ManualResetEventSlim(false);
        private static bool _eventsHooked;

        // ---------------------------------------------------------------- 启停

        // 面板每 2 秒问一次"守护在不在"，守护没跑时要枚举进程；
        // 加个短缓存，避免同一个 tick 里重复枚举。
        private static bool _lastRunning;
        private static DateTime _lastRunningAt = DateTime.MinValue;

        /// <summary>
        /// 让「守护是否在运行」的缓存立刻失效。
        /// IsRunning 有 1.5 秒缓存（避免面板每 2 秒枚举一次进程），
        /// 但用户刚点了开启/关闭，界面必须立刻反映，不能等缓存过期。
        /// </summary>
        public static void InvalidateRunningCache()
        {
            _lastRunningAt = DateTime.MinValue;
        }

        /// <summary>守护是否在运行。先看 pid 文件，再按进程兜底（防 pid 文件丢失留下孤儿）。</summary>
        public static bool IsRunning()
        {
            var now = DateTime.UtcNow;
            if ((now - _lastRunningAt).TotalSeconds < 1.5) return _lastRunning;

            bool result = IsRunningUncached();
            _lastRunning = result;
            _lastRunningAt = now;
            return result;
        }

        private static bool IsRunningUncached()
        {
            var info = ReadPidFile();
            if (info != null)
            {
                try
                {
                    var p = Process.GetProcessById(info.Value.Key);
                    if (info.Value.Value == default(DateTime) ||
                        Math.Abs((p.StartTime - info.Value.Value).TotalSeconds) < 10)
                        return true;
                }
                catch (ArgumentException) { /* 进程已不存在 */ }
                catch (InvalidOperationException) { /* 已退出 */ }
            }

            foreach (var p in FindGuardProcesses())
            {
                if (p.Id != Process.GetCurrentProcess().Id) return true;
            }
            return false;
        }

        public static void Start()
        {
            if (IsRunning()) return;
            var psi = new ProcessStartInfo(Application.ExecutablePath, "-loop")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = AppEnv.Root
            };
            Process.Start(psi);
        }

        public static void Stop()
        {
            var info = ReadPidFile();
            if (info != null)
            {
                try { Process.GetProcessById(info.Value.Key).Kill(); }
                catch (ArgumentException) { /* 已经没了 */ }
                catch (Exception ex) { AppLog.WriteErrorLog("停止守护失败: " + ex.Message); }
            }

            foreach (var p in FindGuardProcesses())
            {
                if (p.Id == Process.GetCurrentProcess().Id) continue;
                try { p.Kill(); } catch (Exception ex) { AppLog.WriteErrorLog("清理守护进程失败: " + ex.Message); }
            }

            Thread.Sleep(250);
            SafeDelete.File_(AppEnv.PidPath);
            Status.Clear();
        }

        private static System.Collections.Generic.KeyValuePair<int, DateTime>? ReadPidFile()
        {
            try
            {
                if (!File.Exists(AppEnv.PidPath)) return null;
                var dict = new JavaScriptSerializer()
                    .Deserialize<System.Collections.Generic.Dictionary<string, object>>(File.ReadAllText(AppEnv.PidPath));
                if (dict == null) return null;

                int pid;
                if (!dict.ContainsKey("pid") || !int.TryParse(Convert.ToString(dict["pid"]), out pid)) return null;

                DateTime start = default(DateTime);
                if (dict.ContainsKey("startTime") && dict["startTime"] != null)
                    DateTime.TryParse(Convert.ToString(dict["startTime"]), out start);

                return new System.Collections.Generic.KeyValuePair<int, DateTime>(pid, start);
            }
            catch (Exception ex)
            {
                AppLog.WriteErrorLog("读 pid 文件失败: " + ex.Message);
                return null;
            }
        }

        private static System.Collections.Generic.List<Process> FindGuardProcesses()
        {
            var list = new System.Collections.Generic.List<Process>();
            try
            {
                foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(Application.ExecutablePath)))
                {
                    try
                    {
                        // 只认自己目录下的同名程序，避免误杀别人的同名程序
                        if (!string.Equals(p.MainModule.FileName, Application.ExecutablePath, StringComparison.OrdinalIgnoreCase))
                            continue;
                        list.Add(p);
                    }
                    catch { /* 权限不足读不到 MainModule：跳过这个进程 */ }
                }
            }
            catch (Exception ex)
            {
                AppLog.WriteErrorLog("枚举守护进程失败: " + ex.Message);
            }
            return list;
        }

        // ---------------------------------------------------------------- 主循环

        public static int Run()
        {
            bool created;
            using (var mutex = new Mutex(true, LoopMutexName, out created))
            {
                if (!created)
                {
                    AppLog.Write("守护已在运行，本次启动退出", "SYS");
                    return 0;
                }

                WritePidFile();
                HookNetworkEvents();
                try
                {
                    AppLog.Write("守护进程已启动", "SYS");
                    int interval = 60;
                    int fails = 0;
                    string prevState = null;
                    DateTime noIpSince = DateTime.MinValue;   // 连着目标 WiFi 却没地址的起始时间
                    int renewTries = 0;                       // 本次无地址期间催过几次 DHCP

                    while (true)
                    {
                        LoginSettings settings = null;
                        string cfgError = null;
                        try
                        {
                            settings = Config.Load().GetLoginSettings();
                            interval = settings.Interval;
                        }
                        catch (Exception ex) { cfgError = ex.Message; }

                        bool online = Network.IsOnline();
                        int nextDelay = interval;

                        if (online)
                        {
                            if (prevState != "online") AppLog.Write("网络正常（已联网）", "NET");
                            if (cfgError != null && prevState != "config_error")
                                AppLog.Write("配置未完成：" + cfgError + "（当前已联网，不影响上网）", "CFG");
                            prevState = "online";
                            fails = 0;
                            Status.Write("online", cfgError == null ? "已联网" : "已联网（" + cfgError + "）", "online", interval);
                        }
                        else if (cfgError != null)
                        {
                            if (prevState != "config_error") AppLog.Write("配置未完成：" + cfgError, "CFG");
                            prevState = "config_error";
                            Status.Write("offline", cfgError, "config", interval);
                        }
                        else if (!Network.IsOnSsid(settings.Ssid))
                        {
                            // 连着别的 WiFi 就别去认证，免得把人家的网络搅乱
                            if (prevState != "skip") AppLog.Write("当前 Wi-Fi 不是 " + settings.Ssid + "，跳过认证", "NET");
                            prevState = "skip";
                            Status.Write("offline", "当前 Wi-Fi 不是 " + settings.Ssid + "，已跳过", "skip", interval);
                        }
                        else if (settings.LoginMode == "dynamic" && string.IsNullOrWhiteSpace(settings.AutoCookies))
                        {
                            if (prevState != "manual") AppLog.Write("动态密码模式：检测到断网，等你点「发送验证码并登录」", "NET");
                            prevState = "manual";
                            Status.Write("offline", "已断网，请点面板上的「发送验证码并登录」", "manual", interval);
                        }
                        else
                        {
                            bool needManual = false;
                            try
                            {
                                var r = LoginRunner.AutoLogin(settings);
                                if (r.Ok && r.Message != "已联网，跳过")
                                {
                                    if (prevState != "login_ok") AppLog.Write(r.Message, "AUTH");
                                    prevState = "login_ok";
                                    fails = 0;
                                    Status.Write("online", r.Message, "login_ok", interval, 0, r.Mode);
                                }
                                else if (r.Ok)
                                {
                                    prevState = "online";
                                    fails = 0;
                                    Status.Write("online", "已联网", "online", interval);
                                }
                                else if (r.Kind == "retry")
                                {
                                    if (prevState != "retry") AppLog.Write(r.Message, "NET");
                                    prevState = "retry";
                                    Status.Write("offline", r.Message, "retry", interval, fails);
                                }
                                else if (r.Kind == "no-ip")
                                {
                                    // 网卡没地址：这时候做任何认证都不可能成功，只能催 DHCP
                                    if (prevState != "no_ip")
                                    {
                                        noIpSince = DateTime.Now;
                                        renewTries = 0;
                                        AppLog.Write("已连接 " + settings.Ssid + " 但没有拿到 IP 地址（DHCP 未响应）", "NET");
                                        prevState = "no_ip";
                                    }

                                    if (renewTries < 3 && (DateTime.Now - noIpSince).TotalSeconds >= 30)
                                    {
                                        renewTries++;
                                        string why;
                                        bool renewed = Network.RenewDhcpForSsid(settings.Ssid, out why);
                                        AppLog.Write(renewed
                                            ? "重新申请 IP 成功（第 " + renewTries + " 次）"
                                            : "重新申请 IP 失败（第 " + renewTries + " 次）：" + why, "NET");
                                        noIpSince = DateTime.Now;   // 重新计时，别连续发
                                    }

                                    Status.Write("waiting",
                                        "已连接 " + settings.Ssid + " 但未获得 IP 地址，正在等待网络分配" +
                                        (renewTries > 0 ? "（已重新申请 " + renewTries + " 次）" : ""),
                                        "waiting-ip", interval, fails);

                                    // 没地址时 5 秒一次没意义，拉长到 10 秒
                                    nextDelay = 10;
                                }
                                else if (r.Kind == "need-manual")
                                {
                                    if (prevState != "manual") AppLog.Write(r.Message, "COOKIE");
                                    prevState = "manual";
                                    needManual = true;
                                    Status.Write("offline", r.Message, "manual", interval, fails);
                                }
                                else
                                {
                                    fails++;
                                    AppLog.Write(r.Message, "AUTH");
                                    prevState = "login_fail";
                                    Status.Write("offline", r.Message, "login_fail", interval, fails);
                                }
                            }
                            catch (Exception ex)
                            {
                                fails++;
                                AppLog.Write("认证异常: " + ex.Message, "ERR");
                                prevState = "error";
                                Status.Write("error", ex.Message, "error", interval, fails);
                            }

                            // 凭据错了再重试也没用，退到最长的间隔
                            nextDelay = ComputeDelay(interval, fails, needManual);
                        }

                        SleepWakeable(nextDelay);
                    }
                }
                finally
                {
                    SafeDelete.File_(AppEnv.PidPath);
                    Status.Clear();
                    AppLog.Write("守护进程已停止", "SYS");
                }
            }
        }

        /// <summary>
        /// 退避策略：成功时用配置的间隔；连续失败按 2^failures 递增，最长 30 分钟。
        /// 需要人工介入时用短间隔，用户操作完能尽快生效。
        /// </summary>
        private static int ComputeDelay(int interval, int fails, bool needManual)
        {
            if (needManual) return Math.Max(5, interval);
            if (fails <= 0) return interval;

            int exp = Math.Min(fails, 6);
            long delay = (long)interval * (1L << exp);
            if (delay > 1800) delay = 1800;
            return (int)delay;
        }

        // ---------------------------------------------------------------- 睡眠 / 唤醒

        private static void HookNetworkEvents()
        {
            if (_eventsHooked) return;
            _eventsHooked = true;
            try
            {
                NetworkChange.NetworkAvailabilityChanged += (s, e) => WakeSignal.Set();
                NetworkChange.NetworkAddressChanged += (s, e) => WakeSignal.Set();
            }
            catch (Exception ex)
            {
                // 订阅不上就退回纯轮询，功能不受影响
                AppLog.WriteErrorLog("订阅网络事件失败: " + ex.Message);
            }
        }

        /// <summary>分片睡眠；期间网络状态有变化就提前醒来。</summary>
        private static void SleepWakeable(int seconds)
        {
            int totalMs = Math.Max(5, seconds) * 1000;
            const int chunk = 500;
            int waited = 0;
            while (waited < totalMs)
            {
                if (WakeSignal.Wait(chunk))
                {
                    WakeSignal.Reset();
                    return;
                }
                waited += chunk;
            }
        }

        private static void WritePidFile()
        {
            try
            {
                var o = new System.Collections.Generic.Dictionary<string, object>
                {
                    { "pid", Process.GetCurrentProcess().Id },
                    { "startTime", Process.GetCurrentProcess().StartTime.ToString("yyyy-MM-dd HH:mm:ss") }
                };
                File.WriteAllText(AppEnv.PidPath, new JavaScriptSerializer().Serialize(o), new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                AppLog.WriteErrorLog("写 pid 文件失败: " + ex.Message);
            }
        }
    }
}