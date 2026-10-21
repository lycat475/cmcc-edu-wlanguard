using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CmccEdu
{
    /// <summary>按钮的三种视觉类型。</summary>
    internal enum UiButtonKind
    {
        Primary,    // 蓝色实心：主要动作
        Secondary,  // 白底描边：次要动作
        Danger      // 白底红字：破坏性动作（下线）
    }

    /// <summary>
    /// 圆角扁平按钮。WinForms 自带的 Button 是 3D 灰块，和现代界面差得远，
    /// 所以这里整块自绘：抗锯齿圆角 + 悬停/按下变色 + 居中文字。
    /// </summary>
    internal sealed class UiButton : Button
    {
        private bool _hover;
        private bool _down;

        public UiButtonKind Kind { get; set; }

        /// <summary>圆角半径，默认 8。高度小于 2 倍半径时自动收缩。</summary>
        public int Radius { get; set; }

        /// <summary>
        /// 按钮下面那层的颜色。圆角四个角会露出来，这里必须填对，
        /// 否则会看到底下残留的像素（实测会露出诡异的蓝色三角）。
        /// </summary>
        public Color SurfaceColor { get; set; }

        public UiButton()
        {
            Kind = UiButtonKind.Secondary;
            Radius = 8;
            SurfaceColor = Color.FromArgb(243, 245, 248);   // 卡片色
            SetStyle(ControlStyles.UserPaint
                   | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Font = new Font("微软雅黑", 9f);
            Cursor = Cursors.Hand;
            UseVisualStyleBackColor = false;
        }

        private Color BackForState()
        {
            if (!Enabled) return Color.FromArgb(243, 244, 246);
            switch (Kind)
            {
                case UiButtonKind.Primary:
                    return _down ? Color.FromArgb(30, 64, 175)
                         : _hover ? Color.FromArgb(29, 78, 216)
                                  : Color.FromArgb(37, 99, 235);
                case UiButtonKind.Danger:
                    return _down ? Color.FromArgb(254, 226, 226)
                         : _hover ? Color.FromArgb(254, 242, 242)
                                  : Color.White;
                default:
                    return _down ? Color.FromArgb(229, 231, 235)
                         : _hover ? Color.FromArgb(243, 244, 246)
                                  : Color.White;
            }
        }

        private Color BorderForState()
        {
            if (!Enabled) return Color.FromArgb(229, 231, 235);
            switch (Kind)
            {
                case UiButtonKind.Primary: return Color.Empty;
                case UiButtonKind.Danger: return Color.FromArgb(252, 205, 205);
                default: return Color.FromArgb(227, 230, 234);
            }
        }

        private Color TextForState()
        {
            if (!Enabled) return Color.FromArgb(156, 163, 175);
            switch (Kind)
            {
                case UiButtonKind.Primary: return Color.White;
                case UiButtonKind.Danger: return Color.FromArgb(220, 38, 38);
                default: return Color.FromArgb(31, 35, 40);
            }
        }

        /// <summary>生成圆角矩形路径（浮点版，便于做半像素偏移）。</summary>
        internal static GraphicsPath RoundedPath(RectangleF r, float radius)
        {
            var path = new GraphicsPath();
            float d = Math.Min(radius * 2f, Math.Min(r.Width, r.Height));
            if (d <= 2f)
            {
                path.AddRectangle(r);
                return path;
            }
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        internal static GraphicsPath RoundedPath(Rectangle r, int radius)
        {
            return RoundedPath(new RectangleF(r.X, r.Y, r.Width, r.Height), radius);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.Half;

            // 关键：1px 描边必须落在像素中心（+0.5），否则抗锯齿会把它摊成
            // 两个半透明像素，看起来发虚、发毛。
            // 先铺满整个矩形：圆角之外的四个角不能留空
            using (var surface = new SolidBrush(SurfaceColor))
                g.FillRectangle(surface, ClientRectangle);

            var rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            using (var path = RoundedPath(rect, Radius))
            {
                using (var brush = new SolidBrush(BackForState()))
                    g.FillPath(brush, path);

                Color border = BorderForState();
                if (border != Color.Empty)
                {
                    using (var pen = new Pen(border, 1f))
                        g.DrawPath(pen, path);
                }
            }

            // 文字用 TextRenderer 画，比 Graphics.DrawString 更清晰（贴近系统渲染）
            TextRenderer.DrawText(
                g, Text, Font,
                new Rectangle(0, 0, Width, Height),
                TextForState(),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs mevent) { _down = true; Invalidate(); base.OnMouseDown(mevent); }
        protected override void OnMouseUp(MouseEventArgs mevent) { _down = false; Invalidate(); base.OnMouseUp(mevent); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    }
}
