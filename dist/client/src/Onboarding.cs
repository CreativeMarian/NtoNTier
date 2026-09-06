using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace NtoNTier
{
    /// <summary>
    /// 轻量首启引导 v2（非侵入）：
    /// - 无全屏遮罩，界面完全可见、可交互；
    /// - 当前步骤目标控件用 accent 色高亮框圈出，目标与引导卡之间画点状连接线；
    /// - 引导卡是不透明圆角卡片，紧贴目标放置（下方→上方），不遮挡关键内容；
    /// - Esc 关闭，Enter 触发「下一步」，点「跳过」结束。
    /// </summary>
    public class FocusTour
    {
        private TourStep[] _steps;
        private int _idx = -1;
        private TourBubblePanel _bubble;
        private TourHighlight _highlight;
        private Form _owner;
        private bool _closed = false;

        public class TourStep
        {
            public Control Target;
            public string Title;
            public string Text;
        }

        public FocusTour(Form owner, TourStep[] steps)
        {
            _owner = owner;
            _steps = steps;
        }

        /// <summary>气泡控件（供自检截图/调试）</summary>
        public Control Bubble { get { return _bubble; } }

        public void Start()
        {
            try
            {
                _highlight = new TourHighlight();
                _highlight.Dock = DockStyle.Fill;
                _owner.Controls.Add(_highlight);
                _highlight.BringToFront();

                _bubble = new TourBubblePanel();
                _bubble.OnNext = Next;
                _bubble.OnSkip = Close;
                _owner.Controls.Add(_bubble);
                _bubble.BringToFront();

                var f = _owner as Form;
                if (f != null && _bubble.NextButton != null) f.AcceptButton = _bubble.NextButton;
                _owner.FormClosed += (s, e) => Close();
                Next();
            }
            catch { Close(); }
        }

        /// <summary>外部请求关闭（如主窗体 Escape）</summary>
        public void RequestClose() { Close(); }

        private void Next()
        {
            try
            {
                if (_idx + 1 >= _steps.Length) { Close(); return; }
                _idx++;
                var s = _steps[_idx];
                if (s.Target == null || s.Target.IsDisposed) { Next(); return; }
                var targetRect = s.Target.RectangleToScreen(s.Target.ClientRectangle);
                var screen = _owner.RectangleToScreen(_owner.ClientRectangle);
                int bw = 320, bh = 152;

                // 气泡放置：优先目标正下方（左对齐目标左缘）→ 上方；横向钳制在窗口内
                int bx = targetRect.Left;
                int by = targetRect.Bottom + 12;
                if (by + bh > screen.Bottom - 8) by = targetRect.Top - bh - 12;
                if (by < screen.Top + 8) by = screen.Top + 8;
                if (bx + bw > screen.Right - 8) bx = screen.Right - bw - 8;
                if (bx < screen.Left + 8) bx = screen.Left + 8;

                _highlight.SetTarget(
                    new Rectangle(_owner.PointToClient(targetRect.Location), targetRect.Size),
                    new Rectangle(_owner.PointToClient(new Point(bx, by)), new Size(bw, bh)));
                _bubble.SetStep(s.Title, s.Text, _idx + 1, _steps.Length);
                _bubble.Bounds = new Rectangle(_owner.PointToClient(new Point(bx, by)), new Size(bw, bh));
                _bubble.Visible = true;
                _bubble.BringToFront();
            }
            catch { Close(); }
        }

        private void Close()
        {
            if (_closed) return;
            _closed = true;
            try { if (_owner is Form) ((Form)_owner).AcceptButton = null; } catch { }
            try { if (_bubble != null && !_bubble.IsDisposed) _bubble.Dispose(); } catch { }
            try { if (_highlight != null && !_highlight.IsDisposed) _highlight.Dispose(); } catch { }
            if (_owner != null && !_owner.IsDisposed) _owner.Invalidate();
            if (Done != null) Done();
        }

        public event Action Done;
    }

    /// <summary>
    /// 穿透高亮层：TopMost 子面板，自绘目标高亮框与连接线。
    /// WM_NCHITTEST 返回 HTTRANSPARENT → 鼠标完全穿透到下层真实控件，
    /// 引导期间界面保持可见、可交互，无任何拦截。
    /// </summary>
    public class TourHighlight : Panel
    {
        private Rectangle _target = Rectangle.Empty;
        private Rectangle _bubble = Rectangle.Empty;

        public TourHighlight()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0084) // WM_NCHITTEST
            {
                m.Result = (IntPtr)(-1); // HTTRANSPARENT: 鼠标穿透
                return;
            }
            base.WndProc(ref m);
        }

        public void SetTarget(Rectangle targetClient, Rectangle bubbleClient)
        {
            _target = targetClient;
            _bubble = bubbleClient;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var t = Theme.Current;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (_target.Width <= 0 || _target.Height <= 0) return;

            var r = _target;
            r.Inflate(5, 5);
            // 外圈淡光 + 主描边
            using (var p = new Pen(Color.FromArgb(55, t.Accent2), 7f))
            using (var path = RoundedPanel.RoundedRect(r, 10))
            {
                g.DrawPath(p, path);
            }
            using (var p = new Pen(t.Accent2, 2f))
            using (var path = RoundedPanel.RoundedRect(r, 10))
            {
                g.DrawPath(p, path);
            }
            // 目标中心 → 气泡中心点状连接线
            if (_bubble.Width > 0 && _bubble.Height > 0)
            {
                var p1 = new Point(_target.X + _target.Width / 2, _target.Y + _target.Height / 2);
                var p2 = new Point(_bubble.X + _bubble.Width / 2, _bubble.Y + _bubble.Height / 2);
                using (var pen = new Pen(Color.FromArgb(95, t.Accent2), 1.5f))
                {
                    pen.DashStyle = DashStyle.Dot;
                    g.DrawLine(pen, p1, p2);
                }
                using (var b = new SolidBrush(t.Accent2))
                {
                    g.FillEllipse(b, p1.X - 3, p1.Y - 3, 6, 6);
                    g.FillEllipse(b, p2.X - 3, p2.Y - 3, 6, 6);
                }
            }
        }
    }

    /// <summary>引导卡（不透明圆角卡片，紧贴目标放置，不遮挡主内容）</summary>
    public class TourBubblePanel : Panel
    {
        private GradientLabel _title;
        private Label _text;
        private Label _step;
        private GButton _btnNext;
        private FlatButton _btnSkip;
        public Action OnNext;
        public Action OnSkip;

        public GButton NextButton { get { return _btnNext; } }

        public TourBubblePanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Size = new Size(320, 152);
            Visible = false;
            BackColor = Color.Transparent;
            Font = new Font("Microsoft YaHei UI", 9.5f);

            _title = new GradientLabel();
            _title.Font = new Font("Microsoft YaHei UI", 12.5f, FontStyle.Bold);
            _title.Location = new Point(20, 14);
            _title.Size = new Size(224, 26);
            Controls.Add(_title);

            _step = new Label();
            _step.Font = new Font("Microsoft YaHei UI", 8.5f);
            _step.ForeColor = Theme.Current.TextFaint;
            _step.Location = new Point(Width - 64, 18);
            _step.Size = new Size(48, 20);
            _step.TextAlign = ContentAlignment.MiddleRight;
            Controls.Add(_step);

            _text = new Label();
            _text.Font = new Font("Microsoft YaHei UI", 9.5f);
            _text.ForeColor = Theme.Current.TextDim;
            _text.Location = new Point(20, 44);
            _text.Size = new Size(Width - 40, 64);
            Controls.Add(_text);

            _btnNext = new GButton();
            _btnNext.Text = "下一步";
            _btnNext.Size = new Size(92, 34);
            _btnNext.Location = new Point(Width - 112, Height - 46);
            _btnNext.Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold);
            _btnNext.Click += (s, e) => { if (OnNext != null) OnNext(); };
            Controls.Add(_btnNext);

            _btnSkip = new FlatButton();
            _btnSkip.Text = "跳过";
            // Soft（实心底）而非 Ghost（透明描边）：卡片自绘背景上 Ghost 合成不可靠
            _btnSkip.Kind = FlatButton.BtnKind.Soft;
            _btnSkip.Size = new Size(66, 34);
            _btnSkip.Location = new Point(20, Height - 46);
            _btnSkip.Click += (s, e) => { if (OnSkip != null) OnSkip(); };
            Controls.Add(_btnSkip);
        }

        public void SetStep(string title, string text, int index, int total)
        {
            _title.Text = title;
            _text.Text = text;
            _btnNext.Text = index == total ? "完成" : "下一步";
            _step.Text = index + " / " + total;
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var t = Theme.Current;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (var b = new SolidBrush(t.CardBg))
            using (var path = RoundedPanel.RoundedRect(rect, 14))
            {
                g.FillPath(b, path);
                using (var p = new Pen(t.Border, 1f)) g.DrawPath(p, path);
            }
            // 顶边 accent 强调条
            using (var pb = new SolidBrush(t.Accent2))
            {
                var bar = new Rectangle(18, 0, Width - 36, 3);
                using (var barPath = RoundedPanel.RoundedRect(bar, 2))
                    g.FillPath(pb, barPath);
            }
        }
    }
}



