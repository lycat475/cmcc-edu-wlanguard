using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CmccEdu
{
    /// <summary>
    /// 带状态圆点的标签。
    /// 之前是用 "●" 字符画的，不同字号/字体下圆心会偏、和文字基线也对不齐；
    /// 改成自己画圆，位置和文字间距都精确可控。
    /// </summary>
    internal sealed class DotLabel : Label
    {
        private Color _dot = Color.Gray;

        /// <summary>圆点直径，默认 10。</summary>
        public int DotSize { get; set; }

        /// <summary>圆点与文字的间距，默认 10。</summary>
        public int DotGap { get; set; }

        /// <summary>圆点颜色；不设置则跟随 ForeColor。</summary>
        public Color DotColor
        {
            get { return _dot; }
            set { _dot = value; Invalidate(); }
        }

        public DotLabel()
        {
            DotSize = 10;
            DotGap = 10;
            SetStyle(ControlStyles.UserPaint
                   | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.ResizeRedraw, true);
        }

        /// <summary>文字起始位置：圆点 + 间距。</summary>
        private int TextLeft { get { return DotSize + DotGap; } }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            // 先铺底色：UserPaint 之后系统不会再帮我们画背景
            using (var bg = new SolidBrush(BackColor))
                g.FillRectangle(bg, ClientRectangle);

            Color dot = DotColor;
            int cy = Height / 2;
            float d = DotSize;

            // 外圈光晕：同色 20% 透明度，比纯圆点更有"活"的感觉
            using (var halo = new SolidBrush(Color.FromArgb(38, dot)))
            {
                float hd = d + 8f;
                g.FillEllipse(halo, (d - hd) / 2f, cy - hd / 2f, hd, hd);
            }

            // 实心圆点
            using (var brush = new SolidBrush(dot))
                g.FillEllipse(brush, 0f, cy - d / 2f, d, d);

            // 文字
            int textWidth = Math.Max(0, Width - TextLeft);
            TextRenderer.DrawText(
                g, Text, Font,
                new Rectangle(TextLeft, 0, textWidth, Height),
                Enabled ? ForeColor : Color.Gray,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        protected override void OnForeColorChanged(EventArgs e)
        {
            // 没单独指定圆点色时，跟随文字色
            if (_dot == Color.Gray) _dot = ForeColor;
            Invalidate();
            base.OnForeColorChanged(e);
        }
    }
}
