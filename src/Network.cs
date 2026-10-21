using System;
using System.Net;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading;
using System.Text.RegularExpressions;

namespace CmccEdu
{
    internal static class Network
    {
        /// <summary>
        /// 是否真的能上外网。
        /// 只判断 HTTP 200 是不行的——Portal 劫持时也会返回 200（认证页面）。
        /// </summary>
        public static bool IsOnline(int timeoutSec = 6)
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create("http://connect.rom.miui.com/generate_204");
                req.Timeout = timeoutSec * 1000;
                req.AllowAutoRedirect = false;
                using (var resp = (HttpWebResponse)req.GetResponse())
                {
                    if ((int)resp.StatusCode == 204) return true;
                }
            }
            catch { /* 连不上就是没网，继续试备用通道 */ }

            try
            {
                var req = (HttpWebRequest)WebRequest.Create("http://www.msftconnecttest.com/connecttest.txt");
                req.Timeout = timeoutSec * 1000;
                using (var resp = (HttpWebResponse)req.GetResponse())
                using (var reader = new System.IO.StreamReader(resp.GetResponseStream()))
                {
                    string body = reader.ReadToEnd();
                    if (body != null && body.Contains("Microsoft Connect Test")) return true;
                }
            }
            catch { /* 两条通道都不通 => 未联网 */ }

            return false;
        }

        [DllImport("iphlpapi.dll", CharSet = CharSet.Auto)]
        private static extern int GetBestInterface(uint destAddr, out uint bestIfIndex);

        /// <summary>默认路由对应的本机 IPv4（用作 Portal 的 wlanuserip）。</summary>
        public static string GetDefaultRouteIp()
        {
            try
            {
                uint idx;
                // 8.8.8.8 的字节序转换：只关心"走哪个接口"
                uint dest = BitConverter.ToUInt32(new byte[] { 8, 8, 8, 8 }, 0);
                if (GetBestInterface(dest, out idx) != 0) return null;

                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    var props = ni.GetIPProperties();
                    if (props.GetIPv4Properties() == null) continue;
                    if (props.GetIPv4Properties().Index != (int)idx) continue;
                    foreach (var ua in props.UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                            return ua.Address.ToString();
                    }
                }
            }
            catch { /* 拿不到就返回空，调用方会走备用逻辑 */ }
            return null;
        }

        /// <summary>指定 SSID 的无线网卡 IPv4 地址；网卡没连上该 SSID 时返回 null。</summary>
        public static string GetSsidAdapterIp(string ssid)
        {
            if (string.IsNullOrEmpty(ssid)) return null;
            try
            {
                string currentGuid = null, hitGuid = null;
                // netsh 输出为本地编码，用 Oem 更稳
                var output = RunNetsh();
                foreach (var rawLine in output.Split('\n'))
                {
                    var line = rawLine.TrimEnd('\r');
                    var mGuid = Regex.Match(line, @"^\s*GUID\s*:\s*(\{?[0-9a-fA-F\-]+\}?)\s*$");
                    if (mGuid.Success) { currentGuid = mGuid.Groups[1].Value; continue; }

                    var mSsid = Regex.Match(line, @"^\s*SSID\s*:\s*(.+?)\s*$");
                    if (mSsid.Success && currentGuid != null &&
                        string.Equals(mSsid.Groups[1].Value.Trim(), ssid, StringComparison.Ordinal))
                    {
                        hitGuid = currentGuid;
                        break;
                    }
                }
                if (hitGuid == null) return null;

                string want = hitGuid.Trim('{', '}').ToLowerInvariant();
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (!string.Equals(ni.Id.ToLowerInvariant(), want, StringComparison.Ordinal)) continue;
                    foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                            return ua.Address.ToString();
                    }
                }
            }
            catch { /* netsh 不可用或解析失败：当没拿到 IP 处理 */ }
            return null;
        }

        /// <summary>
        /// 当前是否连在指定 SSID 上。判断不出来时返回 true（宁可尝试认证，也不要误判为"不在目标网络"）。
        /// </summary>
        /// <summary>
        /// 按 SSID 找到对应的网卡名（例如 "WLAN"），用于 ipconfig 之类的命令。
        /// 找不到返回 null。
        /// </summary>
        public static string GetSsidAdapterName(string ssid)
        {
            if (string.IsNullOrEmpty(ssid)) return null;
            try
            {
                string currentGuid = null, hitGuid = null;
                var output = RunNetsh();
                foreach (var rawLine in output.Split('\n'))
                {
                    var line = rawLine.TrimEnd('\r');
                    var mGuid = Regex.Match(line, @"^\s*GUID\s*:\s*(\{?[0-9a-fA-F\-]+\}?)\s*$");
                    if (mGuid.Success) { currentGuid = mGuid.Groups[1].Value; continue; }

                    var mSsid = Regex.Match(line, @"^\s*SSID\s*:\s*(.+?)\s*$");
                    if (mSsid.Success && currentGuid != null &&
                        string.Equals(mSsid.Groups[1].Value.Trim(), ssid, StringComparison.Ordinal))
                    {
                        hitGuid = currentGuid;
                        break;
                    }
                }
                if (hitGuid == null) return null;

                string want = hitGuid.Trim('{', '}').ToLowerInvariant();
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (string.Equals(ni.Id.ToLowerInvariant(), want, StringComparison.Ordinal))
                        return ni.Name;
                }
                return null;
            }
            catch { return null; }
        }

        /// <summary>
        /// 让指定 SSID 的网卡重新向 DHCP 申请地址，并等待最多 10 秒看是否拿到。
        /// 只在「连着目标 WiFi 但没有 IP」时调用，所以不会打断正常网络。
        /// </summary>
        /// <param name="ssid">目标 WiFi 名。</param>
        /// <param name="message">失败原因。</param>
        public static bool RenewDhcpForSsid(string ssid, out string message)
        {
            message = null;

            string adapterName = GetSsidAdapterName(ssid);
            if (string.IsNullOrEmpty(adapterName))
            {
                message = "找不到对应的网卡";
                return false;
            }

            try
            {
                var psi = new ProcessStartInfo("ipconfig.exe", "/renew \"" + adapterName + "\"")
                {
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using (var proc = Process.Start(psi))
                {
                    if (proc == null) { message = "无法启动 ipconfig"; return false; }
                    if (!proc.WaitForExit(15000))
                    {
                        try { proc.Kill(); } catch { }
                        message = "ipconfig 超时";
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return false;
            }

            // 不解析 ipconfig 的输出（中文系统编码不稳），直接看网卡有没有拿到地址
            for (int i = 0; i < 20; i++)
            {
                if (!string.IsNullOrEmpty(GetSsidAdapterIp(ssid))) return true;
                Thread.Sleep(500);
            }

            message = "重新申请后仍然没有地址";
            return false;
        }

        public static bool IsOnSsid(string ssid)
        {
            if (string.IsNullOrWhiteSpace(ssid)) return true;
            try
            {
                var names = new System.Collections.Generic.List<string>();
                foreach (var rawLine in RunNetsh().Split('\n'))
                {
                    var m = Regex.Match(rawLine.TrimEnd('\r'), @"^\s*SSID\s*:\s*(.+?)\s*$");
                    if (m.Success)
                    {
                        var name = m.Groups[1].Value.Trim();
                        if (name.Length > 0) names.Add(name);
                    }
                }
                if (names.Count == 0) return true;   // 没连无线（有线）或解析失败
                return names.Contains(ssid);
            }
            catch (Exception ex)
            {
                AppLog.WriteErrorLog("读取 SSID 失败: " + ex.Message);
                return true;
            }
        }
        private static string RunNetsh()
        {
            var psi = new System.Diagnostics.ProcessStartInfo("netsh", "wlan show interfaces")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardOutputEncoding = System.Text.Encoding.Default
            };
            using (var p = System.Diagnostics.Process.Start(psi))
            {
                string text = p.StandardOutput.ReadToEnd();
                p.WaitForExit(3000);
                return text;
            }
        }
    }
}
