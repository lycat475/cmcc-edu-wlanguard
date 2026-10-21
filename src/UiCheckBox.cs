using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CmccEdu
{
    /// <summary>
    /// 自绘复选框：圆角方框 + 选中时蓝底白勾，和按钮/输入框同一套风格。
    /// 系统默认的 CheckBox 是 13px 的小方块，边缘还有 1px 的凹陷，和这套界面明显不搭。
    /// </summary>
    internal sealed class UiCheckBox : CheckBox
    {
        private bool _hover;

        /// <summary>方框边长，默认 16。</summary>
        public int BoxSize { get; set; }

        /// <summary>方框与文字的间距，默认 8。</summary>
        public int BoxGap { get; set; }

        /// <summary>卡片底色，用来铺满控件背景。</summary>
        public Color SurfaceColor { get; set; }

        public UiCheckBox()
        {
            BoxSize = 16;
            BoxGap = 8;
            AutoSize = false;
            Font = new Font("微软雅黑", 9f);
            ForeColor = Color.FromArgb(31, 35, 40);
            Cursor = Cursors.Hand;
            SurfaceColor = Color.FromArgb(243, 245, 248);
            SetStyle(ControlStyles.UserPaint
                   | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.Half;

            using (var bg = new SolidBrush(SurfaceColor.IsEmpty ? BackColor : SurfaceColor))
                g.FillRectangle(bg, ClientRectangle);

            int box = BoxSize;
            float top = (Height - box) / 2f;

            var boxRect = new RectangleF(0.5f, top + 0.5f, box - 1f, box - 1f);
            using (var path = UiButton.RoundedPath(boxRect, 4))
            {
                Color fill = Checked ? AccentColor() : Color.White;
                Color edge = Checked ? AccentColor()
                           : _hover ? Color.FromArgb(156, 163, 175)
                                    : Color.FromArgb(203, 208, 214);

                using (var brush = new SolidBrush(Enabled ? fill : Color.FromArgb(243, 244, 246)))
                    g.FillPath(brush, path);
                using (var pen = new Pen(Enabled ? edge : Color.FromArgb(229, 231, 235), 1f))
                    g.DrawPath(pen, path);
            }

            if (Checked)
            {
                // 勾：两段线，圆头
                using (var pen = new Pen(Color.White, 1.9f))
                {
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    g.DrawLines(pen, new PointF[]
                    {
                        new PointF(4.5f,            top + box * 0.52f),
                        new PointF(box * 0.44f,     top + box - 4.5f),
                        new PointF(box - 3.5f,      top + 4.2f)
                    });
                }
            }

            int textLeft = box + BoxGap;
            TextRenderer.DrawText(
                g, Text, Font,
                new Rectangle(textLeft, 0, Math.Max(0, Width - textLeft), Height),
                Enabled ? ForeColor : Color.FromArgb(156, 163, 175),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        private static Color AccentColor()
        {
            return Color.FromArgb(37, 99, 235);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    }
}
