using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CmccEdu
{
    /// <summary>
    /// 圆角输入框外框。
    /// WinForms 的 TextBox 边框只能是 None / FixedSingle / Fixed3D 三种，
    /// 没法画圆角，所以套一层自绘 Panel：Panel 负责圆角边框，TextBox 无边框放在里面。
    /// </summary>
    internal sealed class RoundedField : Panel
    {
        private Color _border = Color.FromArgb(227, 230, 234);
        private Color _fill = Color.White;
        private bool _focused;

        public int Radius { get; set; }

        /// <summary>
        /// 外框下面那层的颜色。圆角外的四个角会露出来，
        /// 用 Parent.BackColor 是错的——父容器是窗体（白），而实际背景是卡片（浅灰）。
        /// </summary>
        public Color SurfaceColor { get; set; }
        public Color FillColor { get { return _fill; } set { _fill = value; Invalidate(); } }

        public Color BorderColor
        {
            get { return _focused ? Color.FromArgb(147, 180, 245) : _border; }
            set { _border = value; Invalidate(); }
        }

        /// <summary>内部控件获得焦点时边框变蓝。</summary>
        public void SetFocused(bool on)
        {
            if (_focused == on) return;
            _focused = on;
            Invalidate();
        }

        public RoundedField()
        {
            Radius = 8;
            SurfaceColor = Color.FromArgb(243, 245, 248);
            SetStyle(ControlStyles.UserPaint
                   | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.ResizeRedraw, true);
            BackColor = Color.White;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.Half;

            // 先铺满，圆角外不能留空
            using (var surface = new SolidBrush(SurfaceColor.IsEmpty ? BackColor : SurfaceColor))
                g.FillRectangle(surface, ClientRectangle);

            var rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            using (var path = UiButton.RoundedPath(rect, Radius))
            {
                using (var brush = new SolidBrush(_fill))
                    g.FillPath(brush, path);
                using (var pen = new Pen(BorderColor, 1f))
                    g.DrawPath(pen, path);
            }
        }
    }
}
