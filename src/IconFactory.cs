using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace CmccEdu
{
    /// <summary>
    /// 托盘/窗口图标。wifi.ico 的 256×256 帧四周有大量透明留白，
    /// 且 ICO 自带的位图帧被 .NET 读出来是花屏，所以统一从源图渲染。
    /// </summary>
    internal static class IconFactory
    {
        // 源图里图形实际占的范围（去掉透明边）
        private static readonly Rectangle ContentRect = new Rectangle(32, 85, 193, 134);

        private static Image _source;
        private static readonly object Gate = new object();

        private static Image Source
        {
            get
            {
                lock (Gate)
                {
                    if (_source == null)
                    {
                        string path = Path.Combine(AppEnv.Root, "assets", "wifi.ico");
                        // 一次读完就断开文件句柄，避免占住文件
                        using (var raw = Image.FromFile(path))
                        {
                            var copy = new Bitmap(raw.Width, raw.Height, PixelFormat.Format32bppArgb);
                            using (var g = Graphics.FromImage(copy))
                            {
                                g.Clear(Color.Transparent);
                                g.DrawImage(raw, 0, 0, raw.Width, raw.Height);
                            }
                            _source = copy;
                        }
                    }
                    return _source;
                }
            }
        }

        public static Bitmap CreateBitmap(int size, Color? dot)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.HighQuality;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);

                // 等比铺满。边距只留 2%：标题栏和托盘里的图标本身就小，
                // 留白越多图形越显小（状态点画在图形外侧，不吃这个边距）
                int margin = Math.Max(1, (int)Math.Round(size * 0.02));
                int target = size - margin * 2;
                double scale = Math.Min((double)target / ContentRect.Width, (double)target / ContentRect.Height);
                int w = Math.Max(1, (int)Math.Round(ContentRect.Width * scale));
                int h = Math.Max(1, (int)Math.Round(ContentRect.Height * scale));
                int x = (size - w) / 2;
                int y = (size - h) / 2;

                g.DrawImage(Source, new Rectangle(x, y, w, h), ContentRect, GraphicsUnit.Pixel);

                if (dot.HasValue)
                {
                    int d = Math.Max(3, (int)Math.Round(size * 0.20));
                    using (var brush = new SolidBrush(dot.Value))
                        g.FillEllipse(brush, size - d, 0, d, d);
                }
            }
            return bmp;
        }

        // 图标按 (尺寸, 颜色) 缓存。
        // Icon.FromHandle 拿到的是我们自己的 HICON，不会自动释放；
        // 托盘每 2 秒刷新一次，不缓存的话每天泄 4 万多个 GDI 句柄，跑几小时就会崩。
        private static readonly Dictionary<string, Icon> IconCache = new Dictionary<string, Icon>();

        public static Icon CreateIcon(int size, Color? dot = null)
        {
            string key = size + "|" + (dot.HasValue ? dot.Value.ToArgb().ToString() : "none");
            lock (Gate)
            {
                Icon cached;
                if (IconCache.TryGetValue(key, out cached) && cached != null) return cached;

                using (var bmp = CreateBitmap(size, dot))
                {
                    var icon = Icon.FromHandle(bmp.GetHicon());
                    IconCache[key] = icon;
                    return icon;
                }
            }
        }

        /// <summary>释放缓存里的图标句柄（进程退出时调用，避免留下来不及回收的句柄）。</summary>
        public static void ReleaseAll()
        {
            lock (Gate)
            {
                foreach (var icon in IconCache.Values)
                {
                    try { if (icon != null) icon.Dispose(); } catch { /* 退出阶段，失败也无所谓 */ }
                }
                IconCache.Clear();
            }
        }

        public static readonly Color DotOk    = Color.FromArgb(52, 199, 89);
        public static readonly Color DotWarn  = Color.FromArgb(255, 159, 10);
        public static readonly Color DotBad   = Color.FromArgb(255, 69, 58);
        public static readonly Color DotIdle  = Color.FromArgb(175, 175, 175);
    }
}
