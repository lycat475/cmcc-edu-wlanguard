using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace CmccEdu
{
    /// <summary>
    /// 托盘/窗口图标。统一从 app.ico 渲染，而不是直接用 ICO 自带的位图帧
    /// （早期 wifi.ico 的帧被 .NET 读出来是花屏，那套逻辑保留至今）。
    /// 图形在源图里的位置用运行时扫描 alpha 算出来，换图标文件也不用改代码。
    /// </summary>
    internal static class IconFactory
    {
        /// <summary>源图里图形实际占的范围（去掉透明边），加载时算一次。</summary>
        private static Rectangle _content = Rectangle.Empty;

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
                        string path = Path.Combine(AppEnv.Root, "assets", "app.ico");
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
                            _content = ComputeContentRect(copy);
                        }
                    }
                    return _source;
                }
            }
        }

        /// <summary>扫描非透明像素，算出图形在源图里的包围盒。</summary>
        private static Rectangle ComputeContentRect(Bitmap bmp)
        {
            int minX = bmp.Width, minY = bmp.Height, maxX = -1, maxY = -1;

            for (int y = 0; y < bmp.Height; y++)
            {
                for (int x = 0; x < bmp.Width; x++)
                {
                    if (bmp.GetPixel(x, y).A <= 8) continue;
                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                }
            }

            if (maxX < 0) return new Rectangle(0, 0, bmp.Width, bmp.Height);   // 全透明就退回整张图
            return new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
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

                // 不留边距：托盘格子只有 16px，留 1px 就等于少 14% 的显示面积。
                // 图形本身是宽扁的（127:90），等比缩放已经占了大部分空间，再留白就更显小。
                int target = size;
                double scale = Math.Min((double)target / _content.Width, (double)target / _content.Height);
                int w = Math.Max(1, (int)Math.Round(_content.Width * scale));
                int h = Math.Max(1, (int)Math.Round(_content.Height * scale));
                int x = (size - w) / 2;
                int y = (size - h) / 2;

                g.DrawImage(Source, new Rectangle(x, y, w, h), _content, GraphicsUnit.Pixel);

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
