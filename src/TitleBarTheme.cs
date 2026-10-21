using System;
using System.Drawing;
using System.Runtime.InteropServices;

namespace CmccEdu
{
    /// <summary>
    /// 用 DWM 给系统标题栏着色。
    /// Win11（Build 22000+）支持 DWMWA_CAPTION_COLOR 等属性，可以把标题栏刷成和窗体一致的白色，
    /// 视觉上就和内容连成一片了，不用自己实现无边框窗口（那样要赔上拖动、阴影、贴边）。
    /// 老系统上调用会失败，静默忽略即可。
    /// </summary>
    internal static class TitleBarTheme
    {
        private const int DWMWA_BORDER_COLOR = 34;
        private const int DWMWA_CAPTION_COLOR = 35;
        private const int DWMWA_TEXT_COLOR = 36;

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        /// <summary>COLORREF 是 0x00BBGGRR，和 Color.ToArgb 的顺序相反。</summary>
        private static int ToColorRef(Color c)
        {
            return c.R | (c.G << 8) | (c.B << 16);
        }

        public static void Apply(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return;

            try
            {
                int caption = ToColorRef(Color.White);
                DwmSetWindowAttribute(hwnd, DWMWA_CAPTION_COLOR, ref caption, sizeof(int));

                int text = ToColorRef(Color.FromArgb(31, 35, 40));
                DwmSetWindowAttribute(hwnd, DWMWA_TEXT_COLOR, ref text, sizeof(int));

                int border = ToColorRef(Color.FromArgb(232, 235, 239));
                DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref border, sizeof(int));
            }
            catch
            {
                // 老系统没有这些属性，保持系统默认标题栏
            }
        }
    }
}
