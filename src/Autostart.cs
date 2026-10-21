using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Xml;

namespace CmccEdu
{
    /// <summary>
    /// 开机自启（计划任务）。等价于旧版 PowerShell 的 CMCC-EDU AutoLogin / CMCC-EDU Client，
    /// 但指向新的 exe。
    /// </summary>
    internal static class Autostart
    {
        public const string TaskGuard = "CMCC-EDU AutoLogin";
        public const string TaskClient = "CMCC-EDU Client";

        /// <summary>任务是否存在（不管是否启用）。</summary>
        public static bool Exists(string taskName)
        {
            try
            {
                var psi = new ProcessStartInfo("schtasks",
                    "/Query /TN \"" + taskName + "\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                using (var p = Process.Start(psi))
                {
                    p.StandardOutput.ReadToEnd();
                    p.StandardError.ReadToEnd();
                    p.WaitForExit(5000);
                    return p.ExitCode == 0;
                }
            }
            catch (Exception ex)
            {
                AppLog.WriteErrorLog("查询计划任务失败: " + ex.Message);
                return false;
            }
        }

        /// <summary>创建或删除任务。-On=true 创建/覆盖，false 删除。</summary>
        public static bool Set(string taskName, bool on, string arguments, string description)
        {
            try
            {
                if (!on)
                {
                    if (!Exists(taskName)) return true;
                    return RunSchtasks("/Delete /TN \"" + taskName + "\" /F");
                }

                string xmlPath = Path.Combine(Path.GetTempPath(), "cmcc_task_" + Guid.NewGuid().ToString("N") + ".xml");
                File.WriteAllText(xmlPath, BuildXml(arguments, description), new UnicodeEncoding(false, true));
                try
                {
                    return RunSchtasks("/Create /XML \"" + xmlPath + "\" /TN \"" + taskName + "\" /F");
                }
                finally
                {
                    SafeDelete.File_(xmlPath);
                }
            }
            catch (Exception ex)
            {
                AppLog.WriteErrorLog("设置计划任务失败: " + ex.Message);
                return false;
            }
        }

        private static bool RunSchtasks(string args)
        {
            var psi = new ProcessStartInfo("schtasks", args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using (var p = Process.Start(psi))
            {
                string stdout = p.StandardOutput.ReadToEnd();
                string stderr = p.StandardError.ReadToEnd();
                p.WaitForExit(10000);
                if (p.ExitCode != 0)
                {
                    AppLog.WriteErrorLog("schtasks 失败(" + p.ExitCode + "): " + stdout + " " + stderr);
                    return false;
                }
                return true;
            }
        }

        private static string BuildXml(string arguments, string description)
        {
            string user = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-16\"?>");
            sb.Append("<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">");
            sb.Append("<RegistrationInfo><Description>");
            sb.Append(Escape(description));
            sb.Append("</Description></RegistrationInfo>");
            sb.Append("<Triggers><LogonTrigger><Enabled>true</Enabled><UserId>");
            sb.Append(Escape(user));
            sb.Append("</UserId><Delay>PT10S</Delay></LogonTrigger></Triggers>");
            sb.Append("<Principals><Principal id=\"Author\"><UserId>");
            sb.Append(Escape(user));
            sb.Append("</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>");
            sb.Append("<Settings>");
            sb.Append("<MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>");
            sb.Append("<DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>");
            sb.Append("<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>");
            sb.Append("<AllowHardTerminate>true</AllowHardTerminate>");
            sb.Append("<StartWhenAvailable>true</StartWhenAvailable>");
            sb.Append("<RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>");
            sb.Append("<IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>");
            sb.Append("<Enabled>true</Enabled>");
            sb.Append("<Hidden>false</Hidden>");
            sb.Append("<ExecutionTimeLimit>PT0S</ExecutionTimeLimit>");
            sb.Append("</Settings>");
            sb.Append("<Actions Context=\"Author\"><Exec><Command>");
            sb.Append(Escape(System.Windows.Forms.Application.ExecutablePath));
            sb.Append("</Command><Arguments>");
            sb.Append(Escape(arguments));
            sb.Append("</Arguments><WorkingDirectory>");
            sb.Append(Escape(AppEnv.Root));
            sb.Append("</WorkingDirectory></Exec></Actions>");
            sb.Append("</Task>");
            return sb.ToString();
        }

        private static string Escape(string s)
        {
            if (s == null) return "";
            return System.Security.SecurityElement.Escape(s);
        }

        // ---------------------------------------------------------------- 便捷封装

        public static bool SetGuard(bool on)
        {
            return Set(TaskGuard, on, "-loop", "登录后自动启动 CMCC-EDU 网络守护");
        }

        /// <summary>-Tray 让面板启动后直接进托盘。</summary>
        public static bool SetClient(bool on, bool startHidden)
        {
            return Set(TaskClient, on, startHidden ? "-tray" : "", "登录后自动启动 CMCC-EDU 网络守护面板");
        }
    }
}
