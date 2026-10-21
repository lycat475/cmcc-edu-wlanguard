using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace CmccEdu
{
    /// <summary>
    /// 自绘下拉框。
    /// WinForms 的 ComboBox 边框和箭头都由系统画，没法做成圆角，
    /// 所以这里整个自绘：圆角底 + 当前值 + 手绘箭头，点开用 ContextMenuStrip 选项。
    /// </summary>
    internal sealed class DropDownField : Control
    {
        private readonly List<string> _items = new List<string>();
        private int _index = -1;
        private bool _hover;
        private LayeredMenu _menu;

        public event EventHandler SelectedIndexChanged;

        public int Radius { get; set; }

        public DropDownField()
        {
            Radius = 8;
            SetStyle(ControlStyles.UserPaint
                   | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.ResizeRedraw, true);
            Font = new Font("微软雅黑", 9f);
            // 不要设 Color.Transparent：Control 需要 SupportsTransparentBackColor 样式，
            // 否则构造时直接抛 ArgumentException（实测导致面板启动即崩）。反正整块自己画。
            BackColor = Color.White;
            Cursor = Cursors.Hand;
        }

        public void AddRange(object[] items)
        {
            if (items == null) return;
            foreach (var it in items) _items.Add(it == null ? "" : it.ToString());
            Invalidate();
        }

        public int SelectedIndex
        {
            get { return _index; }
            set
            {
                if (value < -1 || value >= _items.Count) return;
                if (_index == value) return;
                _index = value;
                Invalidate();
                var h = SelectedIndexChanged;
                if (h != null) h(this, EventArgs.Empty);
            }
        }

        public string SelectedText
        {
            get { return (_index >= 0 && _index < _items.Count) ? _items[_index] : ""; }
        }

        /// <summary>卡片背景色，用来填圆角外的四个角。</summary>
        public Color SurfaceColor { get; set; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.Half;

            Color surface = SurfaceColor.IsEmpty
                ? (Parent != null ? Parent.BackColor : Color.White)
                : SurfaceColor;
            using (var bg = new SolidBrush(surface))
                g.FillRectangle(bg, ClientRectangle);

            var rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            using (var path = UiButton.RoundedPath(rect, Radius))
            {
                using (var fill = new SolidBrush(Color.White))
                    g.FillPath(fill, path);
                using (var pen = new Pen(BorderColor(), 1f))
                    g.DrawPath(pen, path);
            }

            // 右侧箭头
            int arrowX = Width - 20;
            int cy = Height / 2;
            using (var pen = new Pen(Color.FromArgb(107, 114, 128), 1.6f))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawLines(pen, new PointF[]
                {
                    new PointF(arrowX,     cy - 2f),
                    new PointF(arrowX + 4f, cy + 2.5f),
                    new PointF(arrowX + 8f, cy - 2f)
                });
            }

            // 当前值
            var textRect = new Rectangle(10, 0, Width - 32, Height);
            TextRenderer.DrawText(g, SelectedText, Font, textRect, Color.FromArgb(31, 35, 40),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        private Color BorderColor()
        {
            if (Focused) return Color.FromArgb(147, 180, 245);
            if (_hover) return Color.FromArgb(199, 203, 207);
            return Color.FromArgb(227, 230, 234);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            ShowMenu();
        }

        private void ShowMenu()
        {
            if (_items.Count == 0) return;

            var entries = new List<MenuEntry>();
            for (int i = 0; i < _items.Count; i++)
            {
                int idx = i;
                entries.Add(new MenuEntry
                {
                    Text = _items[i],
                    Checked = (i == _index),
                    Click = delegate { SelectedIndex = idx; }
                });
            }

            // 分层窗口菜单：圆角是真正抗锯齿的，不像 Region 裁剪那样有阶梯
            _menu = new LayeredMenu(entries);
            _menu.FormClosed += delegate { Invalidate(); };
            // 减去阴影宽度，菜单圆角边到控件底的视觉距离才是 6px
            _menu.ShowAt(PointToScreen(new Point(0, Height + 6 - LayeredMenu.ShadowSize)));
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing && _menu != null) { _menu.Dispose(); _menu = null; }
            base.Dispose(disposing);
        }
    }
}
