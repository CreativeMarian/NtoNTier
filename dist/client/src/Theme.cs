using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace NtoNTier
{
    /// <summary>
    /// 设计系统（极简扁平）：参照 FreeToken 类桌面软件的轻量风格——
    /// 近白/浅灰底、细边框、小字号、克制留白、单一靛蓝强调色。
    /// 所有自绘控件在 OnPaint 实时读取 Theme.Current，切主题自动重绘。
    /// </summary>
    public class Theme
    {
        public bool IsDark;
        public Color WindowBg, SidebarBg, TitleBg, CardBg, CardAlt, InputBg, Border;
        public Color Text, TextDim, TextFaint;
        public Color Accent1, Accent2, Hover, Press, Danger, DangerHover, Success, Warn;
        public Color ScrollTrack, ScrollThumb;

        public static Theme Dark = new Theme
        {
            IsDark = true,
            WindowBg = Color.FromArgb(22, 24, 29),
            SidebarBg = Color.FromArgb(18, 20, 25),
            TitleBg = Color.FromArgb(18, 20, 25),
            CardBg = Color.FromArgb(30, 33, 39),
            CardAlt = Color.FromArgb(35, 38, 46),
            InputBg = Color.FromArgb(35, 38, 46),
            Border = Color.FromArgb(46, 50, 60),
            Text = Color.FromArgb(232, 234, 238),
            TextDim = Color.FromArgb(156, 163, 175),
            TextFaint = Color.FromArgb(102, 110, 124),
            Accent1 = Color.FromArgb(124, 131, 242),
            Accent2 = Color.FromArgb(154, 124, 240),
            Hover = Color.FromArgb(39, 43, 52),
            Press = Color.FromArgb(30, 33, 39),
            Danger = Color.FromArgb(226, 84, 97),
            DangerHover = Color.FromArgb(240, 106, 118),
            Success = Color.FromArgb(74, 222, 128),
            Warn = Color.FromArgb(245, 192, 74),
            ScrollTrack = Color.FromArgb(22, 24, 29),
            ScrollThumb = Color.FromArgb(58, 63, 75),
        };

        public static Theme Light = new Theme
        {
            IsDark = false,
            WindowBg = Color.FromArgb(245, 246, 248),
            SidebarBg = Color.White,
            TitleBg = Color.White,
            CardBg = Color.White,
            CardAlt = Color.FromArgb(247, 248, 250),
            InputBg = Color.FromArgb(241, 243, 246),
            Border = Color.FromArgb(229, 231, 235),
            Text = Color.FromArgb(25, 28, 34),
            TextDim = Color.FromArgb(94, 102, 115),
            TextFaint = Color.FromArgb(154, 161, 172),
            Accent1 = Color.FromArgb(91, 91, 214),
            Accent2 = Color.FromArgb(122, 91, 214),
            Hover = Color.FromArgb(240, 241, 245),
            Press = Color.FromArgb(232, 234, 240),
            Danger = Color.FromArgb(220, 76, 88),
            DangerHover = Color.FromArgb(201, 65, 77),
            Success = Color.FromArgb(23, 163, 74),
            Warn = Color.FromArgb(217, 119, 6),
            ScrollTrack = Color.FromArgb(245, 246, 248),
            ScrollThumb = Color.FromArgb(201, 206, 217),
        };

        public static Theme Current = Dark;
        public static Theme Prev = Dark;

        /// <summary>强调渐变（水平，同色系弱对比）</summary>
        public static LinearGradientBrush Accent(Rectangle r)
        {
            return new LinearGradientBrush(r, Current.Accent1, Current.Accent2, 0f);
        }

        /// <summary>强调渐变（垂直）</summary>
        public static LinearGradientBrush AccentV(Rectangle r)
        {
            return new LinearGradientBrush(r, Current.Accent1, Current.Accent2, 90f);
        }

        /// <summary>强调色 12% 透明底色（选中/标签）</summary>
        public static Color AccentTint()
        {
            return Current.IsDark ? Color.FromArgb(36, 84, 88, 158) : Color.FromArgb(28, 91, 91, 214);
        }

        /// <summary>强调色加深透明底（导航选中态，视觉更明确）</summary>
        public static Color AccentTintStrong()
        {
            return Current.IsDark ? Color.FromArgb(58, 84, 88, 158) : Color.FromArgb(46, 91, 91, 214);
        }

        public static void Switch(Form f, bool dark)
        {
            Prev = Current;
            Current = dark ? Dark : Light;
            Apply(f);
        }

        public static void Apply(Control root)
        {
            if (root == null || root.IsDisposed) return;
            foreach (Control c in root.Controls)
            {
                ApplyTo(c);
                if (c.HasChildren) Apply(c);
            }
            root.Invalidate(true);
        }

        public static void ApplyTo(Control c)
        {
            var t = Current;
            if (c is GradientLabel) { c.BackColor = Color.Transparent; return; }
            if (c is GButton) { ((GButton)c).Invalidate(); return; }
            if (c is FlatButton) { ((FlatButton)c).Invalidate(); return; }
            if (c is RoundedPanel)
            {
                var rp = (RoundedPanel)c;
                c.BackColor = rp.Fill == Color.Empty ? t.CardBg : rp.Fill;
                return;
            }
            if (c is Panel)
            {
                // 显式设为 Border 色的 1px 分隔线等保留为边框色
                if (Prev != null && c.BackColor == Prev.Border) { c.BackColor = t.Border; return; }
                c.BackColor = t.WindowBg;
                return;
            }
            if (c is NavItem) { ((NavItem)c).Invalidate(); return; }
            if (c is SegmentedControl) { ((SegmentedControl)c).Invalidate(); return; }
            if (c is ThemedCheckBox) { ((ThemedCheckBox)c).Invalidate(); return; }
            if (c is RoundedTextBox) { ((RoundedTextBox)c).ApplyTheme(); return; }
            if (c is TitleBar) { ((TitleBar)c).Invalidate(); return; }
            if (c is OdComboBox) { c.BackColor = t.InputBg; c.ForeColor = t.Text; c.Invalidate(); return; }
            if (c is ListView) { c.BackColor = t.CardBg; c.ForeColor = t.Text; c.Invalidate(); return; }
            if (c is RichTextBox) { c.BackColor = t.InputBg; c.ForeColor = t.Text; return; }
            if (c is TextBox) { c.BackColor = t.InputBg; c.ForeColor = t.Text; return; }
            if (c is Label)
            {
                c.BackColor = Color.Transparent;
                if (Prev != null)
                {
                    Color f = c.ForeColor;
                    if (f == Prev.TextDim) c.ForeColor = t.TextDim;
                    else if (f == Prev.TextFaint) c.ForeColor = t.TextFaint;
                    else if (f == Prev.Text) c.ForeColor = t.Text;
                    else if (f == Prev.Accent1) c.ForeColor = t.Accent1;
                    else if (f == Prev.Accent2) c.ForeColor = t.Accent2;
                    else if (f == Prev.Success) c.ForeColor = t.Success;
                    else if (f == Prev.Warn) c.ForeColor = t.Warn;
                    else if (f == Prev.Danger) c.ForeColor = t.Danger;
                    else c.ForeColor = t.Text;
                }
                else c.ForeColor = t.Text;
                return;
            }
            c.BackColor = t.WindowBg;
            c.ForeColor = t.Text;
        }
    }

    /// <summary>圆角卡片容器（扁平细边框）</summary>
    public class RoundedPanel : Panel
    {
        public int Radius = 12;
        public Color Fill = Color.Empty;
        public bool ShowBorder = false;

        protected override void OnPaint(PaintEventArgs e)
        {
            var t = Theme.Current;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color fill = Fill == Color.Empty ? t.CardBg : Fill;
            using (var b = new SolidBrush(fill))
            {
                g.FillRectangle(b, 0, 0, Width, Height);
                using (var path = RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), Radius))
                {
                    g.FillPath(b, path);
                    if (ShowBorder) using (var p = new Pen(t.Border, 1f)) g.DrawPath(p, path);
                }
            }
        }

        public static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    /// <summary>主操作按钮：强调渐变实心（低对比）</summary>
    public class GButton : Button
    {
        private bool _hover, _pressed;
        public int Radius = 9;

        public GButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            Font = new Font("Microsoft YaHei UI", 10.5f, FontStyle.Bold);
            Height = 42;
        }
        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var t = Theme.Current;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var b = new SolidBrush(t.CardAlt)) g.FillRectangle(b, 0, 0, Width, Height);
            using (var path = RoundedPanel.RoundedRect(rect, Radius))
            {
                if (Enabled)
                {
                    using (var b = Theme.Accent(rect))
                    {
                        g.FillPath(b, path);
                        if (_hover) using (var hb = new SolidBrush(Color.FromArgb(22, Color.White))) g.FillPath(hb, path);
                        if (_pressed) using (var pb = new SolidBrush(Color.FromArgb(34, Color.Black))) g.FillPath(pb, path);
                    }
                }
                else
                {
                    using (var b = new SolidBrush(t.CardAlt)) g.FillPath(b, path);
                }
            }
            TextRenderer.DrawText(g, Text, Font, rect, Enabled ? Color.White : t.TextFaint,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>次级按钮：Ghost（描边）/ Soft（底色）/ Danger（危险红）</summary>
    public class FlatButton : Button
    {
        public enum BtnKind { Ghost, Soft, Danger }
        private BtnKind _kind = BtnKind.Soft;
        private bool _hover, _pressed;
        public int Radius = 9;

        public BtnKind Kind
        {
            get { return _kind; }
            set { _kind = value; Invalidate(); }
        }

        /// <summary>自定义背景色：Ghost 非悬停时用此色填充（如侧栏底部按钮填 SidebarBg），避免透明合成产生残留伪影</summary>
        public Color Fill = Color.Empty;

        public FlatButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            // 不使用 SupportsTransparentBackColor：按钮完全自绘、不透明，
            // 杜绝 DPI 缩放下父级内容被合成进按钮区域（如页脚"更多"残影）。
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            Font = new Font("Microsoft YaHei UI", 9.5f);
            Height = 36;
            BackColor = Color.Empty;
        }
        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var t = Theme.Current;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fg = t.TextDim;
            using (var path = RoundedPanel.RoundedRect(rect, Radius))
            {
                switch (_kind)
                {
                    case BtnKind.Ghost:
                        using (var b = new SolidBrush(_hover ? t.Hover : (Fill == Color.Empty ? t.CardBg : Fill))) g.FillPath(b, path);
                        using (var p = new Pen(_hover ? t.TextDim : t.Border, 1f)) g.DrawPath(p, path);
                        fg = _hover ? t.Text : t.TextDim;
                        break;
                    case BtnKind.Soft:
                        using (var b = new SolidBrush(_pressed ? t.Press : (_hover ? t.Hover : t.CardAlt))) g.FillPath(b, path);
                        fg = _hover ? t.Text : t.TextDim;
                        break;
                    case BtnKind.Danger:
                        using (var b = new SolidBrush(_hover ? t.DangerHover : t.Danger)) g.FillPath(b, path);
                        fg = Color.White;
                        break;
                }
            }
            TextRenderer.DrawText(g, Text, Font, rect, Enabled ? fg : t.TextFaint,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>带占位符的内嵌文本框（无边框）</summary>
    public class PhTextBox : TextBox
    {
        public string Placeholder = "";

        public PhTextBox()
        {
            BorderStyle = BorderStyle.None;
            Font = new Font("Microsoft YaHei UI", 9.5f);
            // 注意：不能加 UserPaint，否则原生 TextBox 的文本绘制会失效。
            // 占位符由外层 RoundedTextBox 直接写入原生 Text（native 渲染，聚焦清除、失焦空时恢复）。
            SetStyle(ControlStyles.OptimizedDoubleBuffer, true);
        }
        protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
        protected override void OnEnter(EventArgs e) { Invalidate(); base.OnEnter(e); }
        protected override void OnLeave(EventArgs e) { Invalidate(); base.OnLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Text.Length == 0 && !Focused && Placeholder.Length > 0)
            {
                TextRenderer.DrawText(e.Graphics, Placeholder, Font, ClientRectangle,
                    Enabled ? Theme.Current.TextFaint : Color.FromArgb(120, Theme.Current.TextFaint),
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }
    }

    /// <summary>自绘下拉框（双主题一致）</summary>
    public class OdComboBox : ComboBox
    {
        public OdComboBox()
        {
            DropDownStyle = ComboBoxStyle.DropDownList;
            DrawMode = DrawMode.OwnerDrawFixed;
            ItemHeight = 26;
            FlatStyle = FlatStyle.Flat;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
            Font = new Font("Microsoft YaHei UI", 9.5f);
        }

        protected override void OnDrawItem(DrawItemEventArgs e)
        {
            var t = Theme.Current;
            var g = e.Graphics;
            if (e.Index < 0 || e.Index >= Items.Count) { e.DrawBackground(); return; }
            Color bg = (e.State & DrawItemState.Selected) != 0 ? t.Accent2 : t.WindowBg;
            Color fg = (e.State & DrawItemState.Selected) != 0 ? Color.White : t.Text;
            using (var b = new SolidBrush(bg)) g.FillRectangle(b, e.Bounds);
            TextRenderer.DrawText(g, Items[e.Index].ToString(), Font,
                new Rectangle(e.Bounds.X + 8, e.Bounds.Y, e.Bounds.Width - 12, e.Bounds.Height), fg,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var t = Theme.Current;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(t.InputBg)) g.FillRectangle(b, ClientRectangle);
            int ax = Width - 22, midY = Height / 2;
            using (var p = new Pen(t.TextDim, 1.6f))
            {
                g.DrawLine(p, ax - 5, midY - 2, ax, midY + 2);
                g.DrawLine(p, ax, midY + 2, ax + 5, midY - 2);
            }
            if (SelectedItem != null)
            {
                TextRenderer.DrawText(g, SelectedItem.ToString(), Font, new Rectangle(10, 0, Width - 34, Height), t.Text,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }
    }

    /// <summary>侧栏导航项（扁平文字行：选中=强调色+左侧指示条）</summary>
    public class NavItem : Control
    {
        public string Key = "";
        public bool Active = false;
        public EventHandler OnClickNav;
        private bool _hover = false;

        private static readonly Font _fActive = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold);

        public NavItem()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            Font = new Font("Microsoft YaHei UI", 10);
            Height = 46;
        }
        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseClick(MouseEventArgs e) { if (OnClickNav != null) OnClickNav(this, EventArgs.Empty); base.OnMouseClick(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var t = Theme.Current;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = new SolidBrush(t.SidebarBg)) g.FillRectangle(b, 0, 0, Width, Height);
            var row = new Rectangle(8, 2, Width - 16, Height - 4);
            if (_hover || Active)
            {
                using (var b = new SolidBrush(Active ? Theme.AccentTintStrong() : t.Hover))
                using (var path = RoundedPanel.RoundedRect(row, 8)) g.FillPath(b, path);
            }
            // 左侧指示条（选中，加粗）
            if (Active)
            {
                using (var b = new SolidBrush(t.Accent1))
                using (var path = RoundedPanel.RoundedRect(new Rectangle(4, (Height - 18) / 2, 4, 18), 2)) g.FillPath(b, path);
            }
            TextRenderer.DrawText(g, Text, Active ? _fActive : Font, new Rectangle(26, 0, Width - 34, Height),
                Active ? t.Accent1 : (_hover ? t.Text : t.TextDim),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }

    /// <summary>渐变文字标签（仅 Logo 等少量品牌元素）</summary>
    public class GradientLabel : Label
    {
        public GradientLabel()
        {
            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            AutoSize = false;
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var b = Theme.Accent(ClientRectangle))
            {
                g.DrawString(Text, Font, b, ClientRectangle,
                    new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter });
            }
        }
    }

    /// <summary>圆点状态指示（● 带文字）</summary>
    public class StatusDot : Control
    {
        private string _text = "";
        public Color DotColor = Color.FromArgb(154, 161, 172);
        public Font TextFont = new Font("Microsoft YaHei UI", 10f);

        public void Set(string text, Color color)
        {
            _text = text;
            DotColor = color;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var t = Theme.Current;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int cx = 9, cy = Height / 2;
            using (var b = new SolidBrush(Color.FromArgb(36, DotColor))) g.FillEllipse(b, cx - 6, cy - 6, 12, 12);
            using (var b = new SolidBrush(DotColor)) g.FillEllipse(b, cx - 4, cy - 4, 8, 8);
            TextRenderer.DrawText(g, _text, TextFont, new Rectangle(24, 0, Width - 28, Height), t.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        }
    }
}

