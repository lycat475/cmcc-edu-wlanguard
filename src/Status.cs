using System;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;

namespace CmccEdu
{
    /// <summary>运行时状态，面板靠读它显示「守护运行中/网络状态/最近结果」。</summary>
    internal static class Status
    {
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        public static void Write(string state, string message, string action, int interval, int fails = 0, string mode = "")
        {
            try
            {
                var o = new System.Collections.Generic.Dictionary<string, object>
                {
                    { "pid", System.Diagnostics.Process.GetCurrentProcess().Id },
                    { "time", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") },
                    { "state", state },
                    { "action", action },
                    { "message", message },
                    { "interval", interval },
                    { "fails", fails }
                };
                if (!string.IsNullOrEmpty(mode)) o["mode"] = mode;

                string text = new JavaScriptSerializer().Serialize(o);
                string tmp = AppEnv.StatusPath + ".tmp";
                File.WriteAllText(tmp, text, Utf8NoBom);

                if (File.Exists(AppEnv.StatusPath))
                {
                    try { File.Replace(tmp, AppEnv.StatusPath, null); return; }
                    catch { try { File.Delete(AppEnv.StatusPath); } catch { } }
                }
                File.Move(tmp, AppEnv.StatusPath);
            }
            catch (Exception ex)
            {
                // 状态写不进去面板会显示"未知"，值得记一笔
                AppLog.WriteErrorLog("写状态失败: " + ex.Message);
            }
        }

        /// <summary>读状态；文件不存在或内容损坏都返回 null。</summary>
        public static StatusSnapshot Read()
        {
            try
            {
                if (!File.Exists(AppEnv.StatusPath)) return null;
                string text = File.ReadAllText(AppEnv.StatusPath, Encoding.UTF8);
                var dict = new JavaScriptSerializer().Deserialize<System.Collections.Generic.Dictionary<string, object>>(text);
                if (dict == null) return null;

                var s = new StatusSnapshot();
                s.Pid     = ToInt(dict, "pid");
                s.Time    = ToStr(dict, "time");
                s.State   = ToStr(dict, "state");
                s.Action  = ToStr(dict, "action");
                s.Message = ToStr(dict, "message");
                s.Interval = ToInt(dict, "interval");
                s.Fails   = ToInt(dict, "fails");
                s.Mode    = ToStr(dict, "mode");
                return s;
            }
            catch (Exception ex)
            {
                AppLog.WriteErrorLog("读状态失败: " + ex.Message);
                return null;
            }
        }

        public static void Clear()
        {
            try { File.Delete(AppEnv.StatusPath); } catch { }
        }

        private static string ToStr(System.Collections.Generic.Dictionary<string, object> d, string k)
        {
            object v;
            return d.TryGetValue(k, out v) && v != null ? Convert.ToString(v) : "";
        }

        private static int ToInt(System.Collections.Generic.Dictionary<string, object> d, string k)
        {
            int n;
            object v;
            return d.TryGetValue(k, out v) && v != null && int.TryParse(Convert.ToString(v), out n) ? n : 0;
        }
    }

    internal sealed class StatusSnapshot
    {
        public int Pid;
        public string Time;
        public string State;
        public string Action;
        public string Message;
        public int Interval;
        public int Fails;
        public string Mode;
    }
}
