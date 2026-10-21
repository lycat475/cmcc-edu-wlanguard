using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CmccEdu
{
    /// <summary>菜单里的一项。</summary>
    internal sealed class MenuEntry
    {
        public string Text;
        public bool IsSeparator;
        public bool Checked;
        public Action Click;

        public static MenuEntry Sep()
        {
            return new MenuEntry { IsSeparator = true };
        }
    }

    /// <summary>
    /// 分层窗口菜单（per-pixel alpha）。
    /// ContextMenuStrip 想做成圆角只能用 Region 裁剪，而 Region 不做抗锯齿，边缘是阶梯。
    /// 这里改成自己画一张 32 位带 alpha 的位图，用 UpdateLayeredWindow 贴上去，
    /// 圆角、边框、阴影都是真正的平滑边缘。
    /// </summary>
    internal sealed class LayeredMenu : Form
    {
        private const int WS_EX_LAYERED = 0x00080000;
        private const int ULW_ALPHA = 0x00000002;
        private const byte AC_SRC_ALPHA = 0x01;

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct SIZE { public int cx, cy; }

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        private struct BLENDFUNCTION
        {
            public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst,
            ref POINT pptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc,
            int crKey, ref BLENDFUNCTION pblend, int dwFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hdc);

        private const int RowHeight = 30;
        private const int SepHeight = 11;
        private const int PadY = 6;
        private const int PadX = 14;
        private const int CheckW = 22;
        private const int Radius = 8;
        private const int Shadow = 0;   // 不画阴影，窗口边缘就是菜单边缘

        /// <summary>窗口四周的透明阴影宽度。外部定位时要减去它，否则视觉间距会多出一圈。</summary>
        public const int ShadowSize = Shadow;

        private readonly List<MenuEntry> _entries = new List<MenuEntry>();
        private readonly Font _font = new Font("微软雅黑", 10f);
        private readonly int _width;
        private readonly int _height;
        private int _hover = -1;
        private System.Windows.Forms.Timer _closeWatch;
        private bool _armed;   // 见过一次鼠标松开后才允许因点外部而关闭

        public LayeredMenu(IEnumerable<MenuEntry> entries)
        {
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            AutoScaleMode = AutoScaleMode.None;

            foreach (var e in entries) _entries.Add(e);

            // 量一遍宽度
            int textW = 0;
            using (var bmp = new Bitmap(1, 1))
            using (var g = Graphics.FromImage(bmp))
            {
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                foreach (var e in _entries)
                {
                    if (e.IsSeparator) continue;
                    var sz = g.MeasureString(e.Text, _font);
                    if ((int)Math.Ceiling(sz.Width) > textW) textW = (int)Math.Ceiling(sz.Width);
                }
            }

            bool hasCheck = false;
            foreach (var e in _entries) if (!e.IsSeparator && e.Checked) { hasCheck = true; break; }

            int contentW = PadX * 2 + textW + 8 + (hasCheck ? CheckW : 0);
            _width = Math.Max(140, contentW) + Shadow * 2;

            int h = PadY * 2 + Shadow * 2;
            foreach (var e in _entries) h += e.IsSeparator ? SepHeight : RowHeight;
            _height = h;

            Size = new Size(_width, _height);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED;
                return cp;
            }
        }

        /// <summary>在屏幕坐标处弹出。</summary>
        public void ShowAt(Point screenPoint)
        {
            // 超出屏幕时往上/往左挪
            var wa = Screen.FromPoint(screenPoint).WorkingArea;
            int x = Math.Min(screenPoint.X, wa.Right - _width);
            int y = Math.Min(screenPoint.Y, wa.Bottom - _height);
            if (x < wa.Left) x = wa.Left;
            if (y < wa.Top) y = wa.Top;

            Location = new Point(x, y);
            Show();
            Render();

            // 托盘右键弹菜单时，本进程可能没拿到前台权限：窗口显示出来但没被激活，
            // 点别处不会触发 Deactivate，菜单会一直挂着。这里主动抢一次焦点兜底。
            try { Activate(); SetForegroundWindow(Handle); }
            catch (Exception ex) { AppLog.Write("菜单抢焦点失败：" + ex.Message, "UI"); }

            _closeWatch = new System.Windows.Forms.Timer { Interval = 80 };
            _closeWatch.Tick += delegate
            {
                // 打开菜单那一下鼠标还按着（下拉框是 MouseDown 弹出的），
                // 此时光标在菜单外会被当成“点了别处”而误关。
                // 所以必须先见到一次鼠标松开，才开始监听外部点击。
                if (Control.MouseButtons == MouseButtons.None) { _armed = true; return; }
                if (!_armed) return;
                if (Bounds.Contains(Cursor.Position)) return;
                Close();
            };
            _closeWatch.Start();
        }

        private void Render()
        {
            using (var bmp = new Bitmap(_width, _height, System.Drawing.Imaging.PixelFormat.Format32bppPArgb))
            {
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.PixelOffsetMode = PixelOffsetMode.Half;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    g.Clear(Color.Transparent);

                    var body = new Rectangle(Shadow, Shadow, _width - Shadow * 2, _height - Shadow * 2);


                    using (var path = UiButton.RoundedPath(
                        new RectangleF(body.X + 0.5f, body.Y + 0.5f, body.Width - 1f, body.Height - 1f), Radius))
                    {
                        using (var fill = new SolidBrush(Color.White))
                            g.FillPath(fill, path);
                        using (var pen = new Pen(Color.FromArgb(227, 230, 234), 1f))
                            g.DrawPath(pen, path);
                    }

                    // 内容
                    float y = body.Y + PadY;
                    for (int i = 0; i < _entries.Count; i++)
                    {
                        var e = _entries[i];
                        if (e.IsSeparator)
                        {
                            using (var pen = new Pen(Color.FromArgb(232, 235, 239), 1f))
                                g.DrawLine(pen, body.X + 12, y + SepHeight / 2f, body.Right - 12, y + SepHeight / 2f);
                            y += SepHeight;
                            continue;
                        }

                        if (i == _hover)
                        {
                            using (var hp = UiButton.RoundedPath(
                                new RectangleF(body.X + 4.5f, y + 1f, body.Width - 8f, RowHeight - 2f), 6))
                            using (var hb = new SolidBrush(Color.FromArgb(239, 244, 255)))
                                g.FillPath(hb, hp);
                        }

                        float tx = body.X + PadX + (HasCheck ? CheckW : 0);

                        if (HasCheck)
                        {
                            if (e.Checked)
                            {
                                float cx = body.X + PadX + 3f;
                                float cy = y + RowHeight / 2f;
                                using (var pen = new Pen(Color.FromArgb(37, 99, 235), 1.7f))
                                {
                                    pen.StartCap = LineCap.Round;
                                    pen.EndCap = LineCap.Round;
                                    g.DrawLines(pen, new PointF[]
                                    {
                                        new PointF(cx,        cy),
                                        new PointF(cx + 3.8f, cy + 3.6f),
                                        new PointF(cx + 9.2f, cy - 4.2f)
                                    });
                                }
                            }
                            else tx = body.X + PadX + CheckW - 10;   // 未选中项与选中项文字对齐
                        }

                        // 用实测字高做垂直居中。StringFormat.LineAlignment 居中的是"行框"，
                        // 行框含上下留白，中文字形会显得偏上或偏下。
                        var textSize = g.MeasureString(e.Text, _font);
                        float ty = y + (RowHeight - textSize.Height) / 2f;
                        g.DrawString(e.Text, _font, Brushes.Black, new PointF(tx, ty));

                        y += RowHeight;
                    }
                }

                ApplyLayered(bmp);
            }
        }

        private bool HasCheck
        {
            get
            {
                foreach (var e in _entries) if (!e.IsSeparator && e.Checked) return true;
                return false;
            }
        }

        private void ApplyLayered(Bitmap bmp)
        {
            IntPtr screenDc = GetDC(IntPtr.Zero);
            IntPtr memDc = CreateCompatibleDC(screenDc);
            IntPtr hBitmap = bmp.GetHbitmap(Color.FromArgb(0));
            IntPtr oldBitmap = SelectObject(memDc, hBitmap);

            try
            {
                var size = new SIZE { cx = bmp.Width, cy = bmp.Height };
                var src = new POINT { X = 0, Y = 0 };
                var dst = new POINT { X = Left, Y = Top };
                var blend = new BLENDFUNCTION
                {
                    BlendOp = 0,
                    BlendFlags = 0,
                    SourceConstantAlpha = 255,
                    AlphaFormat = AC_SRC_ALPHA
                };

                UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, ULW_ALPHA);
            }
            finally
            {
                SelectObject(memDc, oldBitmap);
                DeleteObject(hBitmap);
                DeleteDC(memDc);
                ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr h);

        /// <summary>把客户区 Y 转成项下标。</summary>
        private int IndexAt(int clientY)
        {
            float y = Shadow + PadY;
            for (int i = 0; i < _entries.Count; i++)
            {
                float h = _entries[i].IsSeparator ? SepHeight : RowHeight;
                if (clientY >= y && clientY < y + h)
                    return _entries[i].IsSeparator ? -1 : i;
                y += h;
            }
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int idx = IndexAt(e.Y);
            if (idx != _hover) { _hover = idx; Render(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hover != -1) { _hover = -1; Render(); }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            int idx = IndexAt(e.Y);
            if (idx < 0 || idx >= _entries.Count) { Close(); return; }

            var entry = _entries[idx];
            Close();
            if (entry.Click != null) entry.Click();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.KeyCode == Keys.Escape) { Close(); return; }

            if (e.KeyCode == Keys.Down || e.KeyCode == Keys.Up)
            {
                // 全是分隔符时没有可选项，直接忽略，否则下面的循环会转不出来
                bool hasItem = false;
                foreach (var it in _entries) { if (!it.IsSeparator) { hasItem = true; break; } }
                if (!hasItem) return;

                int dir = e.KeyCode == Keys.Down ? 1 : -1;
                int idx = _hover;
                int guard = 0;   // 兜底：不管数据多奇怪，最多绕两圈就放弃
                do
                {
                    idx += dir;
                    if (idx < 0) idx = _entries.Count - 1;
                    if (idx >= _entries.Count) idx = 0;
                    if (++guard > _entries.Count * 2) return;
                }
                while (_entries[idx].IsSeparator);

                _hover = idx;
                Render();
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Enter && _hover >= 0 && _hover < _entries.Count)
            {
                var entry = _entries[_hover];
                Close();
                if (entry.Click != null) entry.Click();
                e.Handled = true;
            }
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            Close();
        }

        protected override void Dispose(bool disposing)
        {
            // 菜单是每次打开都新建的，字体必须跟着释放，否则每开一次漏一个 GDI 句柄
            if (disposing)
            {
                if (_closeWatch != null) { _closeWatch.Stop(); _closeWatch.Dispose(); _closeWatch = null; }
                _font.Dispose();
            }
            base.Dispose(disposing);
        }

        protected override bool ShowWithoutActivation
        {
            get { return false; }
        }
    }
}
