using System;
using System.IO;

namespace CmccEdu
{
    /// <summary>
    /// 删除文件的统一入口。
    /// 原来散落各处的 <c>try { File.Delete(x); } catch { }</c> 既看不出"为什么允许失败"，
    /// 也没法统一排查。这里集中处理：文件不存在直接返回，删除失败记一行日志。
    /// </summary>
    internal static class SafeDelete
    {
        /// <summary>删除文件；不存在或删不掉都不抛异常。</summary>
        /// <param name="path">文件路径。</param>
        /// <param name="tag">日志标签，默认 DEL。</param>
        public static void File_(string path, string tag = "DEL")
        {
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
            }
            catch (Exception ex)
            {
                AppLog.Write("删除失败 " + path + "：" + ex.Message, tag);
            }
        }

        /// <summary>尝试把一个操作当成"尽力而为"，失败只记日志。</summary>
        /// <param name="action">要执行的操作。</param>
        /// <param name="what">日志里显示的动作描述。</param>
        /// <param name="tag">日志标签。</param>
        public static void Try(Action action, string what, string tag = "DEL")
        {
            if (action == null) return;
            try
            {
                action();
            }
            catch (Exception ex)
            {
                AppLog.Write(what + " 失败：" + ex.Message, tag);
            }
        }
    }
}
