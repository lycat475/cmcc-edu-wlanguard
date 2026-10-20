using System;
using System.IO;
using System.Text;

namespace CmccEdu
{
    /// <summary>
    /// 日志。格式与旧版保持一致，方便直接对比：
    ///   2026-10-20 21:25:16  [AUTH] cookie 自动登录成功
    /// </summary>
    internal static class AppLog
    {
        private static readonly object Gate = new object();
        private static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        public static void Write(string message, string tag = null)
        {
            var line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  ";
            if (!string.IsNullOrEmpty(tag)) line += "[" + tag + "] ";
            line += message;

            lock (Gate)
            {
                try
                {
                    File.AppendAllText(AppEnv.LogPath, line + "\r\n", Utf8NoBom);
                }
                catch
                {
                    // 日志写不进去不该影响主流程，但也不能完全无声：
                    // 退到错误日志，再失败就放弃（此时磁盘很可能已经不可写）。
                    TryWriteErrorLog(line);
                }
            }
        }

        /// <summary>面板自己的诊断日志（与认证日志分开）。</summary>
        public static void WriteErrorLog(string message)
        {
            lock (Gate) TryWriteErrorLog(DateTime.Now.ToString("HH:mm:ss") + " " + message);
        }

        private static void TryWriteErrorLog(string line)
        {
            try
            {
                File.AppendAllText(AppEnv.ErrorLogPath, line + "\r\n", Utf8NoBom);
            }
            catch
            {
                // 连错误日志都写不了，只能放弃——再抛异常会盖掉真正的错误。
            }
        }
    }
}
