using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CmccEdu
{
    /// <summary>
    /// 细滚动条：宽 8px、圆角滑块，替代 TextBox 自带的 17px 灰色滚动条。
    /// TextBox 关掉 ScrollBars 后仍然可以用滚轮/键盘滚动，我们只负责显示和拖动。
    /// </summary>
    internal sealed class ThinScrollBar : Control
    {
        private const uint EM_GETFIRSTVISIBLELINE = 0x00CE;
        private const uint EM_LINESCROLL = 0x00B6;
        private const uint EM_GETLINECOUNT = 0x00BA;

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        private readonly TextBox _target;
        private bool _hover;
        private bool _dragging;
        private int _dragOffset;

        public ThinScrollBar(TextBox target)
        {
            _target = target;
            Width = 8;
            SetStyle(ControlStyles.UserPaint
                   | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.ResizeRedraw, true);
            BackColor = Color.White;
            Cursor = Cursors.Default;

            target.TextChanged += delegate { Sync(); };
            target.MouseWheel += delegate { BeginInvoke(new Action(Sync)); };
            target.KeyUp += delegate { Sync(); };
            target.Resize += delegate { Sync(); };
        }

        /// <summary>线高（像素）。用字体高度近似。</summary>
        private float LineHeight
        {
            get
            {
                using (var g = _target.CreateGraphics())
                    return g.MeasureString("汉", _target.Font).Height;
            }
        }

        private int TotalLines()
        {
            if (!_target.IsHandleCreated) return 0;
            return (int)SendMessage(_target.Handle, EM_GETLINECOUNT, IntPtr.Zero, IntPtr.Zero);
        }

        private int FirstVisibleLine()
        {
            if (!_target.IsHandleCreated) return 0;
            return (int)SendMessage(_target.Handle, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero);
        }

        private int VisibleLines()
        {
            int n = (int)(_target.ClientSize.Height / Math.Max(1f, LineHeight));
            return Math.Max(1, n);
        }

        /// <summary>根据 TextBox 状态重算滑块并重绘。</summary>
        public void Sync()
        {
            Visible = TotalLines() > VisibleLines();
            Invalidate();
        }

        private RectangleF Track()
        {
            return new RectangleF(0, 2, Width, Math.Max(1, Height - 4));
        }

        private RectangleF Thumb()
        {
            var track = Track();
            int total = Math.Max(1, TotalLines());
            int visible = VisibleLines();
            if (total <= visible) return track;

            float ratio = (float)visible / total;
            float thumbH = Math.Max(24f, track.Height * ratio);
            float maxTop = track.Height - thumbH;

            int first = FirstVisibleLine();
            float progress = total - visible <= 0 ? 0f : (float)first / (total - visible);
            if (progress < 0f) progress = 0f;
            if (progress > 1f) progress = 1f;

            return new RectangleF(track.X, track.Y + maxTop * progress, track.Width, thumbH);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            using (var bg = new SolidBrush(BackColor))
                g.FillRectangle(bg, ClientRectangle);

            if (!Visible) return;

            var thumb = Thumb();
            Color color = _dragging ? Color.FromArgb(120, 126, 134)
                        : _hover ? Color.FromArgb(160, 166, 174)
                                 : Color.FromArgb(205, 210, 216);

            using (var path = UiButton.RoundedPath(thumb, 4))
            using (var brush = new SolidBrush(color))
                g.FillPath(brush, path);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            var thumb = Thumb();
            if (thumb.Contains(e.Location))
            {
                _dragging = true;
                _dragOffset = (int)(e.Y - thumb.Y);
            }
            else
            {
                // 点空白：滑块中心跳到点击处
                _dragging = true;
                _dragOffset = (int)(thumb.Height / 2);
                DragTo(e.Y);
            }
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging) DragTo(e.Y);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragging = false;
            Invalidate();
        }

        private void DragTo(int mouseY)
        {
            var track = Track();
            float thumbH = Thumb().Height;
            float maxTop = track.Height - thumbH;
            if (maxTop <= 0) return;

            float top = mouseY - _dragOffset - track.Y;
            if (top < 0) top = 0;
            if (top > maxTop) top = maxTop;

            float progress = top / maxTop;
            int total = TotalLines();
            int visible = VisibleLines();
            int maxFirst = Math.Max(0, total - visible);
            int wantFirst = (int)Math.Round(progress * maxFirst);

            int delta = wantFirst - FirstVisibleLine();
            if (delta != 0)
                SendMessage(_target.Handle, EM_LINESCROLL, IntPtr.Zero, (IntPtr)delta);

            Invalidate();
        }
    }
}
