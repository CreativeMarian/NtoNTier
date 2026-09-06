using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace NtoNTier
{
    /// <summary>
    /// 无边框现代窗口基类：
    /// 保留系统可缩放边框（Sizable），通过 WM_NCCALCSIZE 移除原生标题栏，
    /// 用自绘 TitleBar 呈现标题与窗口按钮；最大化时限制在工作区内。
    /// </summary>
    public class ChromeForm : Form
    {
        private const int WM_NCCALCSIZE = 0x0083;
        private const int WM_GETMINMAXINFO = 0x0024;
        protected int TitleHeight = 42;
        protected TitleBar Title;

        public ChromeForm()
        {
            FormBorderStyle = FormBorderStyle.Sizable;
            DoubleBuffered = true;
            BackColor = Theme.Current.WindowBg;
            Font = new Font("Microsoft YaHei UI", 9.5f);
            Padding = new Padding(0);
            StartPosition = FormStartPosition.CenterScreen;
        }

        /// <summary>挂接自绘标题栏（必须在子类把内容控件加完后调用，保证停靠顺序正确）</summary>
        public void SetupChrome(string title, bool canMax)
        {
            Text = title;
            MaximizeBox = canMax;
            Title = new TitleBar(this);
            Title.TitleText = title;
            Title.Height = TitleHeight;
            Title.CanMaximize = canMax;
            Title.Dock = DockStyle.Top;
            Controls.Add(Title);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                // Win11 圆角窗口角；旧系统忽略
                int pref = 2; // DWMWCP_ROUND
                DwmSetWindowAttribute(Handle, 33, ref pref, 4);
            }
            catch { }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_NCCALCSIZE)
            {
                if (m.WParam != IntPtr.Zero)
                {
                    // 移除原生标题栏/边框占位，客户端铺满整个窗口
                    m.Result = IntPtr.Zero;
                    return;
                }
                base.WndProc(ref m);
                return;
            }
            if (m.Msg == WM_GETMINMAXINFO)
            {
                try
                {
                    var mmi = (MINMAXINFO)Marshal.PtrToStructure(m.LParam, typeof(MINMAXINFO));
                    Screen scr = Screen.FromHandle(Handle);
                    Rectangle wa = scr.WorkingArea;
                    mmi.MaxX = wa.Width;
                    mmi.MaxY = wa.Height;
                    mmi.MaxPosX = wa.Left;
                    mmi.MaxPosY = wa.Top;
                    mmi.MaxTrackX = wa.Width;
                    mmi.MaxTrackY = wa.Height;
                    Marshal.StructureToPtr(mmi, m.LParam, true);
                }
                catch { }
                m.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref m);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MINMAXINFO
        {
            public int ReservedX, ReservedY;
            public int MaxX, MaxY;
            public int MaxPosX, MaxPosY;
            public int MinTrackX, MinTrackY;
            public int MaxTrackX, MaxTrackY;
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
    }

    /// <summary>自绘标题栏：左侧 Logo+标题，右侧 最小化/最大化/关闭；拖拽移动、双击最大化</summary>
    public class TitleBar : Control
    {
        private static readonly Font _logoFont = new Font("Segoe UI", 10, FontStyle.Bold);
        private ChromeForm _owner;
        private bool _hMin, _hMax, _hClose;
        public string TitleText = "";
        public bool CanMaximize = true;

        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wp, IntPtr lp);
        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private const int HTCAPTION = 2;
        private const int WM_NCHITTEST = 0x0084;

        public TitleBar(ChromeForm owner)
        {
            _owner = owner;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Font = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold);
            Cursor = Cursors.Default;
        }

        private Rectangle MinR { get { return new Rectangle(Width - 138, 0, 46, Height); } }
        private Rectangle MaxR { get { return new Rectangle(Width - 92, 0, 46, Height); } }
        private Rectangle CloseR { get { return new Rectangle(Width - 46, 0, 46, Height); } }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool a = MinR.Contains(e.Location), b = CanMaximize && MaxR.Contains(e.Location), c = CloseR.Contains(e.Location);
            if (a != _hMin || b != _hMax || c != _hClose) { _hMin = a; _hMax = b; _hClose = c; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (_hMin || _hMax || _hClose) { _hMin = _hMax = _hClose = false; Invalidate(); }
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                if (MinR.Contains(e.Location)) return;
                if (CanMaximize && MaxR.Contains(e.Location)) return;
                if (CloseR.Contains(e.Location)) return;
                // 其余区域：交给系统做标题栏拖拽（含拖动到顶部最大化、双击最大化）
                ReleaseCapture();
                SendMessage(_owner.Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                if (MinR.Contains(e.Location)) { _owner.WindowState = FormWindowState.Minimized; return; }
                if (CanMaximize && MaxR.Contains(e.Location))
                {
                    _owner.WindowState = _owner.WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
                    return;
                }
                if (CloseR.Contains(e.Location)) { _owner.Close(); return; }
            }
            base.OnMouseUp(e);
        }

        protected override void WndProc(ref Message m)
        {
            // 顶部 6px 让出系统缩放边（左上/上/右上）
            if (m.Msg == WM_NCHITTEST && _owner.WindowState != FormWindowState.Maximized)
            {
                int x = (short)(m.LParam.ToInt64() & 0xFFFF);
                int y = (short)((m.LParam.ToInt64() >> 16) & 0xFFFF);
                Point pt = PointToClient(new Point(x, y));
                if (pt.Y <= 6)
                {
                    int ht = 12; // HTTOP
                    if (pt.X <= 6) ht = 13; // HTTOPLEFT
                    else if (pt.X >= Width - 7) ht = 14; // HTTOPRIGHT
                    m.Result = (IntPtr)ht;
                    return;
                }
            }
            base.WndProc(ref m);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var t = Theme.Current;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(t.TitleBg)) g.FillRectangle(b, 0, 0, Width, Height);

            // 左：渐变 Logo 方块 + 标题
            var logo = new Rectangle(14, (Height - 24) / 2, 24, 24);
            using (var path = RoundedPanel.RoundedRect(logo, 7))
            {
                using (var b = Theme.Accent(logo)) g.FillPath(b, path);
            }
            TextRenderer.DrawText(g, "N", _logoFont, logo, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, TitleText, Font, new Rectangle(46, 0, 320, Height), t.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            // 右侧窗口按钮
            DrawWinBtn(g, MinR, _hMin, false, false);
            DrawWinBtn(g, MaxR, _hMax, true, _owner.WindowState == FormWindowState.Maximized);
            DrawWinBtn(g, CloseR, _hClose, false, false, true);

            // 底部细分隔线
            using (var p = new Pen(t.Border, 1f)) g.DrawLine(p, 0, Height - 1, Width, Height - 1);
        }

        private void DrawWinBtn(Graphics g, Rectangle r, bool hover, bool isMax, bool maximized, bool close = false)
        {
            var t = Theme.Current;
            if (hover)
            {
                using (var b = new SolidBrush(close ? t.Danger : t.Hover))
                {
                    g.FillRectangle(b, r);
                }
            }
            Color c = hover ? Color.White : t.TextDim;
            using (var p = new Pen(c, 1.4f))
            {
                if (close)
                {
                    g.DrawLine(p, r.X + 15, r.Y + 14, r.Right - 15, r.Bottom - 14);
                    g.DrawLine(p, r.Right - 15, r.Y + 14, r.X + 15, r.Bottom - 14);
                }
                else if (isMax)
                {
                    int w = 11, hh = 11;
                    int x = r.X + (r.Width - w) / 2, y = r.Y + (r.Height - hh) / 2;
                    if (maximized)
                    {
                        // 还原：两个叠放矩形
                        g.DrawRectangle(p, x + 2, y - 2, w - 2, hh - 2);
                        g.DrawRectangle(p, x - 2, y + 2, w - 2, hh - 2);
                    }
                    else
                    {
                        g.DrawRectangle(p, x, y, w, hh);
                    }
                }
                else
                {
                    // 最小化：横线
                    int cx = r.X + r.Width / 2, cy = r.Y + r.Height / 2;
                    g.DrawLine(p, cx - 6, cy, cx + 6, cy);
                }
            }
        }
    }

    /// <summary>
    /// 分段选择器（替代原生 TabControl）：圆角容器 + 渐变胶囊页签 + 内容宿主。
    /// </summary>
    public class SegmentedControl : Control
    {
        private List<string> _titles = new List<string>();
        private List<Control> _pages = new List<Control>();
        private Panel _host;
        private int _selected = -1;
        private int _headerH = 44;

        public int SelectedIndex { get { return _selected; } }
        public event EventHandler SelectedIndexChanged;

        public SegmentedControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Font = new Font("Microsoft YaHei UI", 9.5f);
            _host = new Panel();
            _host.Padding = new Padding(16, 12, 16, 12);
            Controls.Add(_host);
            OnResize(EventArgs.Empty);
        }

        public void AddPage(string title, Control content)
        {
            _titles.Add(title);
            content.Dock = DockStyle.Fill;
            _host.Controls.Add(content);
            content.Visible = false;
            _pages.Add(content);
            if (_selected < 0) SelectIndex(0);
            Invalidate();
        }

        public void SelectIndex(int i)
        {
            if (i < 0 || i >= _pages.Count || i == _selected) return;
            _selected = i;
            for (int k = 0; k < _pages.Count; k++) _pages[k].Visible = (k == i);
            Invalidate();
            if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_host != null) _host.Bounds = new Rectangle(0, _headerH, Width, Math.Max(0, Height - _headerH));
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (e.Y < _headerH)
            {
                int x = 8;
                for (int i = 0; i < _titles.Count; i++)
                {
                    int w = TextRenderer.MeasureText(_titles[i], Font).Width + 36;
                    if (e.X >= x && e.X <= x + w) { SelectIndex(i); break; }
                    x += w + 8;
                }
            }
            base.OnMouseClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var t = Theme.Current;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            // 浅灰轨道
            using (var b = new SolidBrush(t.InputBg))
            using (var path = RoundedPanel.RoundedRect(new Rectangle(0, 0, Width - 1, _headerH - 1), 10))
                g.FillPath(b, path);
            int x = 6;
            for (int i = 0; i < _titles.Count; i++)
            {
                int w = TextRenderer.MeasureText(_titles[i], Font).Width + 30;
                var chip = new Rectangle(x, 6, w, _headerH - 12);
                if (i == _selected)
                {
                    using (var b = new SolidBrush(t.CardBg))
                    using (var path = RoundedPanel.RoundedRect(chip, 8)) g.FillPath(b, path);
                    using (var p = new Pen(t.Border, 1f))
                    using (var path = RoundedPanel.RoundedRect(chip, 8)) g.DrawPath(p, path);
                    TextRenderer.DrawText(g, _titles[i], Font, chip, t.Accent1,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                }
                else
                {
                    TextRenderer.DrawText(g, _titles[i], Font, chip, t.TextDim,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                }
                x += w + 4;
            }
        }
    }

    /// <summary>圆角输入框：圆角容器 + 无边框内嵌文本框（带占位符）</summary>
    public class RoundedTextBox : Control
    {
        private PhTextBox _inner;
        private string _placeholder = "";
        private bool _isPh = false;
        private Color _normalFore = Color.Black;
        private Color _phFore = Color.Gray;
        public event EventHandler TextChangedInternal;

        public RoundedTextBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = 34;
            _inner = new PhTextBox();
            Controls.Add(_inner);
            _inner.TextChanged += (s, e) => { OnInnerChanged(); if (TextChangedInternal != null) TextChangedInternal(this, EventArgs.Empty); };
            _inner.Enter += (s, e) => OnInnerEnter();
            _inner.Leave += (s, e) => { _inner.Invalidate(); UpdatePh(); };
            OnResize(EventArgs.Empty);
            ApplyTheme();
            UpdatePh();
        }

        /// <summary>占位符直接写入原生 TextBox（native 渲染，永不变糊），聚焦时清除、失焦空时恢复</summary>
        private void OnInnerChanged()
        {
            if (_isPh && _inner.Text != _placeholder)
            {
                _isPh = false;
                _inner.ForeColor = _normalFore;
            }
            else if (!_isPh)
            {
                UpdatePh();
            }
        }

        private void OnInnerEnter()
        {
            if (_isPh)
            {
                _inner.Text = "";
                _isPh = false;
                _inner.ForeColor = _normalFore;
            }
            _inner.Invalidate();
        }

        private void UpdatePh()
        {
            if (_inner == null) return;
            bool show = _inner.Enabled && _placeholder.Length > 0 && _inner.Text.Length == 0 && !_inner.Focused;
            if (show && !_isPh)
            {
                _inner.Text = _placeholder;
                _isPh = true;
                _inner.ForeColor = _phFore;
            }
            else if (!show && _isPh)
            {
                _inner.Text = "";
                _isPh = false;
                _inner.ForeColor = _normalFore;
            }
        }

        public void ApplyTheme()
        {
            var t = Theme.Current;
            BackColor = t.InputBg;
            _inner.BackColor = t.InputBg;
            _normalFore = t.Text;
            _phFore = t.TextFaint;
            _inner.ForeColor = _isPh ? _phFore : _normalFore;
            _inner.Invalidate();
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_inner != null)
            {
                _inner.Location = new Point(12, 7);
                _inner.Width = Math.Max(10, Width - 24);
                _inner.Height = Math.Max(20, Height - 14);
            }
        }

        public override string Text { get { return _isPh ? "" : _inner.Text; } set { _inner.Text = value ?? ""; _isPh = false; _inner.ForeColor = _normalFore; UpdatePh(); } }
        public string Placeholder { get { return _placeholder; } set { _placeholder = value ?? ""; UpdatePh(); } }
        public bool ReadOnlyInner { get { return _inner.ReadOnly; } set { _inner.ReadOnly = value; } }
        public PhTextBox Inner { get { return _inner; } }
        public new bool Enabled
        {
            get { return base.Enabled; }
            set { base.Enabled = value; _inner.Enabled = value; UpdatePh(); Invalidate(); }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var t = Theme.Current;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = RoundedPanel.RoundedRect(rect, 9))
            {
                using (var b = new SolidBrush(_inner.Enabled ? t.InputBg : t.CardAlt)) g.FillPath(b, path);
                using (var p = new Pen(t.Border, 1f)) g.DrawPath(p, path);
            }
        }
    }

    /// <summary>主题复选框（圆角方块 + 渐变勾选）</summary>
    public class ThemedCheckBox : CheckBox
    {
        public ThemedCheckBox()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            AutoSize = false;
            Height = 28;
            Cursor = Cursors.Hand;
            Font = new Font("Microsoft YaHei UI", 9.5f);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var t = Theme.Current;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(t.WindowBg)) g.FillRectangle(b, 0, 0, Width, Height);
            var box = new Rectangle(3, (Height - 20) / 2, 20, 20);
            using (var path = RoundedPanel.RoundedRect(box, 6))
            {
                if (Checked)
                {
                    using (var b = Theme.Accent(box)) g.FillPath(b, path);
                    using (var pen = new Pen(Color.White, 2.2f))
                    {
                        g.DrawLine(pen, box.X + 5, box.Y + 10, box.X + 8, box.Y + 13);
                        g.DrawLine(pen, box.X + 8, box.Y + 13, box.X + 15, box.Y + 6);
                    }
                }
                else
                {
                    using (var b = new SolidBrush(t.InputBg)) g.FillPath(b, path);
                    using (var p = new Pen(t.Border, 1.2f)) g.DrawPath(p, path);
                }
            }
            TextRenderer.DrawText(g, Text, Font, new Rectangle(32, 0, Width - 38, Height),
                Enabled ? t.Text : t.TextFaint,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }

    /// <summary>只读日志/结果区（等宽字体，双主题）</summary>
    public class LogBox : TextBox
    {
        public LogBox()
        {
            Multiline = true;
            ReadOnly = true;
            ScrollBars = ScrollBars.Both;
            BorderStyle = BorderStyle.None;
            Font = new Font("Consolas", 9.5f);
            WordWrap = false;
            SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
        }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); }
    }

    /// <summary>抽屉式侧滑菜单：半透明遮罩 + 右侧圆角抽屉面板</summary>
    public class DrawerMenu : Control
    {
        private Form _owner;
        private Panel _dim;
        private Timer _slide;
        private int _bodyW = 304;
        private int _margin = 12;
        private string[] _items = new string[0];
        private Action[] _acts = new Action[0];
        private int _hoverIdx = -1;
        private bool _hoverClose = false;
        private bool _closing = false;
        private Font _headerFont = new Font("Microsoft YaHei UI", 13, FontStyle.Bold);
        private Font _itemFont = new Font("Microsoft YaHei UI", 10.5f);

        public DrawerMenu(Form owner)
        {
            _owner = owner;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Size = new Size(_bodyW, Math.Max(120, owner.ClientSize.Height - _margin * 2));
        }

        public void Open(string[] items, Action[] acts)
        {
            _items = items ?? new string[0];
            _acts = acts ?? new Action[0];
            _dim = new Panel();
            _dim.Dock = DockStyle.Fill;
            _dim.BackColor = Color.FromArgb(120, 0, 0, 0);
            _owner.Controls.Add(_dim);
            _dim.BringToFront();
            _dim.Click += (s, e) => Close();

            Location = new Point(_owner.ClientSize.Width, _margin);
            _owner.Controls.Add(this);
            BringToFront();
            _slide = new Timer();
            _slide.Interval = 12;
            _slide.Tick += (s, e) =>
            {
                int target = _owner.ClientSize.Width - _bodyW - _margin;
                if (_closing)
                {
                    if (Location.X < _owner.ClientSize.Width)
                        Location = new Point(Math.Min(_owner.ClientSize.Width, Location.X + 30), Location.Y);
                    else { _slide.Stop(); DisposeAll(); }
                }
                else
                {
                    if (Location.X > target)
                        Location = new Point(Math.Max(target, Location.X - 30), Location.Y);
                    else { Location = new Point(target, Location.Y); _slide.Stop(); }
                }
            };
            _slide.Start();
        }

        public void Close()
        {
            if (_closing) return;
            _closing = true;
            if (_slide == null) { DisposeAll(); return; }
            _slide.Start();
        }

        private void DisposeAll()
        {
            try { if (_dim != null && !_dim.IsDisposed) _dim.Dispose(); } catch { }
            try { if (!IsDisposed) Dispose(); } catch { }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var t = Theme.Current;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var path = RoundedPanel.RoundedRect(rect, 14))
            {
                using (var b = new SolidBrush(t.CardBg)) g.FillRectangle(b, 0, 0, Width, Height);
                using (var b = new SolidBrush(t.CardBg)) g.FillPath(b, path);
                using (var p = new Pen(t.Border, 1f)) g.DrawPath(p, path);
            }
            TextRenderer.DrawText(g, "更多功能", _headerFont, new Rectangle(20, 14, 200, 26), t.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            var cr = new Rectangle(Width - 46, 12, 32, 32);
            if (_hoverClose)
            {
                using (var b = new SolidBrush(t.Hover))
                using (var pth = RoundedPanel.RoundedRect(cr, 8)) g.FillPath(b, pth);
            }
            using (var p = new Pen(t.TextDim, 1.8f))
            {
                g.DrawLine(p, cr.X + 10, cr.Y + 10, cr.Right - 10, cr.Bottom - 10);
                g.DrawLine(p, cr.Right - 10, cr.Y + 10, cr.X + 10, cr.Bottom - 10);
            }
            using (var p = new Pen(t.Border)) g.DrawLine(p, 16, 54, Width - 16, 54);
            int y = 64;
            for (int i = 0; i < _items.Length; i++)
            {
                var ir = new Rectangle(14, y, Width - 28, 48);
                if (i == _hoverIdx)
                {
                    using (var b = new SolidBrush(t.Hover))
                    using (var pth = RoundedPanel.RoundedRect(ir, 10)) g.FillPath(b, pth);
                }
                TextRenderer.DrawText(g, _items[i], _itemFont, ir, t.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                y += 56;
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            var cr = new Rectangle(Width - 46, 12, 32, 32);
            bool hc = cr.Contains(e.Location);
            int idx = (e.Y - 64) / 56;
            if (idx < 0 || idx >= _items.Length) idx = -1;
            if (hc != _hoverClose || idx != _hoverIdx) { _hoverClose = hc; _hoverIdx = idx; Invalidate(); }
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            if (_hoverIdx != -1 || _hoverClose) { _hoverIdx = -1; _hoverClose = false; Invalidate(); }
            base.OnMouseLeave(e);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            var cr = new Rectangle(Width - 46, 12, 32, 32);
            if (cr.Contains(e.Location)) { Close(); return; }
            if (e.Y >= 64)
            {
                int idx = (e.Y - 64) / 56;
                if (idx >= 0 && idx < _items.Length && _acts[idx] != null)
                {
                    var a = _acts[idx];
                    Close();
                    a();
                    return;
                }
            }
            base.OnMouseClick(e);
        }
    }
}
