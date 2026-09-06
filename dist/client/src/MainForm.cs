using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace NtoNTier
{
    /// <summary>主窗口：自定义标题栏 + 扁平侧栏导航 + 多页面</summary>
    public class MainForm : ChromeForm
    {
        private AppConfig _cfg;
        private Theme _t { get { return Theme.Current; } }

        private Panel _sidebar, _content, _pageArea, _sidebarFooter;
        private NavItem _navMain, _navMembers, _navTest, _navFileshare, _navBrowser, _navGuide, _navMore;
        private FlatButton _btnTheme;
        private Label _lblVersion;
        private Panel _statusBar;
        private StatusDot _statusDot;
        private Label _lblStatusHint;
        private Label _lblLinkMode;

        private Panel _pageMain, _pageMembers, _pageTest, _pageFileshare, _pageGuide;
        private BrowserPage _pageBrowser;

        // 联机功能
        private SegmentedControl _serverSeg;
        private OdComboBox _cmbOfficial;
        private FlatButton _btnRefreshLatency;
        private RoundedTextBox _txtCustom, _txtSegment, _txtSuffix, _txtPrefix;
        private GButton _btnConnect;
        private Label _lblSummary;
        private System.Windows.Forms.Timer _statusTimer;

        // 在线成员
        private ListView _listMembers;
        private Label _lblMembersInfo;
        private FlatButton _btnRefresh;

        // 网络测试
        private SegmentedControl _testSeg;
        private RoundedTextBox _txtPingHost, _txtTcpHost, _txtUdpHost, _txtTcpPort, _txtUdpPort;
        private LogBox _txtTestResult;

        // 文件共享 (HFS)
        private RoundedTextBox _txtHfsPort, _txtHfsDir;
        private Label _lblHfsStatus, _lblHfsUrl;
        private FlatButton _btnHfsStart, _btnHfsStop;

        private DrawerMenu _drawer;
        private volatile bool _scanInProgress = false;   // 在线成员网段扫描进行中标志
        private volatile bool _queryInProgress = false;  // edge peer 查询进行中标志（防重叠）
        private int _lastScanTick = -20000;                 // 网段扫描节流（每 20 秒一轮）
        private List<string> _lastScanIps = new List<string>();   // 上次网段扫描结果缓存（防止刷新间隙闪没）
        private bool _supernodeWarned = false;
        private int _refreshCounter = 0;                     // supernode 不可达是否已提示过

        // 自绘 ListView 复用字体（避免每次重绘 new Font 造成 GDI 对象泄漏）
        private static readonly Font _fColHeader = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
        private static readonly Font _fColItem = new Font("Microsoft YaHei UI", 9.5f);

        /// <summary>自检截图模式（--shot 参数触发，跳过首启引导）</summary>
        public bool ShotMode = false;

        public MainForm()
        {
            _cfg = AppConfig.Load();
            BuildUi();
            BuildOfficialList();
            LoadUiFromConfig();
            // 启动时后台测试所有服务器延迟并排序
            RefreshServerLatencies();
            ApplyTheme();
            _statusTimer = new System.Windows.Forms.Timer();
            _statusTimer.Interval = 3000;
            _statusTimer.Tick += (s, e) => RefreshStatus(false);
            _statusTimer.Start();
            FormClosed += (s, e) =>
            {
                try { Net.StopEdge(); } catch { }
                try { HfsManager.Stop(); } catch { }
                try { FileTransfer.StopServer(); } catch { }
                try { if (_pageBrowser != null) _pageBrowser.Shutdown(); } catch { }
            };
            Shown += (s, e) => FirstRunFlow();
        }

        // ==========================================================
        // 布局
        // ==========================================================
        private void BuildUi()
        {
            Text = "NtoNTier - 虚拟局域网联机工具";
            StartPosition = FormStartPosition.CenterScreen;
            MinimumSize = new Size(960, 640);
            Size = new Size(1100, 720);

            _content = new Panel();
            _content.Dock = DockStyle.Fill;
            Controls.Add(_content);

            _sidebar = new Panel();
            _sidebar.Dock = DockStyle.Left;
            _sidebar.Width = 200;
            Controls.Add(_sidebar);

            BuildSidebar();
            BuildContent();

            // 自定义标题栏（最后添加 → 停靠最上，横跨整窗）
            SetupChrome("NtoNTier", true);

            ShowPage("main");
        }

        private void BuildSidebar()
        {
            _navMain = MakeNav("联机功能", "main", 18);
            _navMembers = MakeNav("在线成员", "members", 64);
            _navTest = MakeNav("网络测试", "test", 110);
            _navFileshare = MakeNav("文件共享", "fileshare", 156);
            _navBrowser = MakeNav("文件浏览", "browser", 202);
            _navGuide = MakeNav("使用指南", "guide", 248);
            _navMore = MakeNav("更多", "more", 294);
            _navMore.OnClickNav = (s, e) => ShowMoreMenu();

            var div = new Panel();
            div.Height = 1;
            div.BackColor = _t.Border;
            div.Location = new Point(16, 302);
            div.Width = 168;
            _sidebar.Controls.Add(div);

            // 底部功能栏：停靠侧栏底部，窗口缩放始终贴底（不再依赖 Anchor 计算）
            _sidebarFooter = new Panel();
            _sidebarFooter.Dock = DockStyle.Bottom;
            _sidebarFooter.Height = 96;
            _sidebarFooter.BackColor = _t.SidebarBg;
            _sidebar.Controls.Add(_sidebarFooter);

            _btnTheme = new FlatButton();
            _btnTheme.Kind = FlatButton.BtnKind.Ghost;
            _btnTheme.Fill = _t.SidebarBg;
            _btnTheme.Text = _t.IsDark ? "暗色模式" : "亮色模式";
            _btnTheme.Location = new Point(16, 10);
            _btnTheme.Size = new Size(168, 38);
            _btnTheme.Click += (s, e) => ToggleTheme();
            _sidebarFooter.Controls.Add(_btnTheme);

            _lblVersion = new Label();
            _lblVersion.Text = "NtoNTier v1.4.0 · n2n 3.1";
            _lblVersion.Font = new Font("Microsoft YaHei UI", 8.5f);
            _lblVersion.ForeColor = _t.TextFaint;
            _lblVersion.AutoSize = true;
            _lblVersion.Location = new Point(20, 60);
            _sidebarFooter.Controls.Add(_lblVersion);
        }

        private NavItem MakeNav(string text, string key, int y)
        {
            var n = new NavItem();
            n.Text = text;
            n.Key = key;
            n.Location = new Point(8, y);
            n.Width = _sidebar.Width - 16;
            n.OnClickNav = (s, e) => { if (key == "more") ShowMoreMenu(); else ShowPage(key); };
            _sidebar.Controls.Add(n);
            return n;
        }

        private void BuildContent()
        {
            _pageArea = new Panel();
            _pageArea.Dock = DockStyle.Fill;
            _content.Controls.Add(_pageArea);

            _statusBar = new Panel();
            _statusBar.Dock = DockStyle.Top;
            _statusBar.Height = 46;
            _content.Controls.Add(_statusBar);

            _statusDot = new StatusDot();
            _statusDot.Location = new Point(24, 8);
            _statusDot.Size = new Size(360, 30);
            _statusDot.Set("未连接", _t.TextFaint);
            _statusBar.Controls.Add(_statusDot);

            _lblStatusHint = new Label();
            _lblStatusHint.Text = "优先点对点直连 · 失败自动走中继";
            _lblStatusHint.Font = new Font("Microsoft YaHei UI", 8.5f);
            _lblStatusHint.ForeColor = _t.TextFaint;
            _lblStatusHint.AutoSize = true;
            _lblStatusHint.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _lblStatusHint.Location = new Point(_statusBar.Width - 240, 15);
            _statusBar.Controls.Add(_lblStatusHint);

            _lblLinkMode = new Label();
            _lblLinkMode.Text = "";
            _lblLinkMode.Font = new Font("Microsoft YaHei UI", 8.5f);
            _lblLinkMode.ForeColor = _t.TextFaint;
            _lblLinkMode.AutoSize = true;
            _lblLinkMode.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _lblLinkMode.Location = new Point(_statusBar.Width - 400, 15);
            _statusBar.Controls.Add(_lblLinkMode);

            var line = new Panel();
            line.Height = 1;
            line.BackColor = _t.Border;
            line.Dock = DockStyle.Bottom;
            _statusBar.Controls.Add(line);

            _pageMain = BuildMainPage();
            _pageMembers = BuildMembersPage();
            _pageTest = BuildTestPage();
            _pageFileshare = BuildFilesharePage();
            _pageBrowser = BuildBrowserPage();
            _pageGuide = BuildGuidePage();
            _pageArea.Controls.Add(_pageMain);
            _pageArea.Controls.Add(_pageMembers);
            _pageArea.Controls.Add(_pageTest);
            _pageArea.Controls.Add(_pageFileshare);
            _pageArea.Controls.Add(_pageBrowser);
            _pageArea.Controls.Add(_pageGuide);

            _pageArea.Resize += (s, e) =>
            {
                var r = new Rectangle(0, 0, _pageArea.Width, _pageArea.Height);
                _pageMain.Bounds = r;
                _pageMembers.Bounds = r;
                _pageTest.Bounds = r;
                _pageFileshare.Bounds = r;
                _pageBrowser.Bounds = r;
                _pageGuide.Bounds = r;
            };
        }

        /// <summary>页面标题头（大标题 + 副标题）</summary>
        private Panel MakeHeader(string title, string subtitle)
        {
            var p = new Panel();
            p.Height = 64;
            var t = new Label();
            t.Text = title;
            t.Font = new Font("Microsoft YaHei UI", 15f, FontStyle.Bold);
            t.Location = new Point(26, 10);
            t.AutoSize = true;
            p.Controls.Add(t);
            if (!string.IsNullOrEmpty(subtitle))
            {
                var s = new Label();
                s.Text = subtitle;
                s.Font = new Font("Microsoft YaHei UI", 9f);
                s.ForeColor = _t.TextFaint;
                s.Location = new Point(26, 40);
                s.AutoSize = true;
                p.Controls.Add(s);
            }
            return p;
        }

        private void ShowPage(string key)
        {
            SetNavActive(_navMain, key == "main");
            SetNavActive(_navMembers, key == "members");
            SetNavActive(_navTest, key == "test");
            SetNavActive(_navFileshare, key == "fileshare");
            SetNavActive(_navBrowser, key == "browser");
            SetNavActive(_navGuide, key == "guide");
            _pageMain.Visible = key == "main";
            _pageMembers.Visible = key == "members";
            _pageTest.Visible = key == "test";
            _pageFileshare.Visible = key == "fileshare";
            _pageBrowser.Visible = key == "browser";
            _pageGuide.Visible = key == "guide";
            if (key == "fileshare") RefreshHfsStatus();
        }

        /// <summary>只刷新状态变化的导航项，避免全部重绘</summary>
        private void SetNavActive(NavItem nav, bool active)
        {
            if (nav.Active != active)
            {
                nav.Active = active;
                nav.Invalidate();
            }
        }

        // ==========================================================
        // 联机功能页        // ==========================================================
        private Panel BuildMainPage()
        {
            var page = new Panel();

            // 状态卡（Fill）
            var statusCard = new RoundedPanel();
            statusCard.Dock = DockStyle.Fill;
            statusCard.ShowBorder = true;
            page.Controls.Add(statusCard);

            var lblSt = new Label();
            lblSt.Text = "连接状态";
            lblSt.Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
            lblSt.ForeColor = _t.TextDim;
            lblSt.Location = new Point(26, 20);
            lblSt.AutoSize = true;
            statusCard.Controls.Add(lblSt);

            _lblSummary = new Label();
            _lblSummary.Text = "尚未连接 · 选择服务器并点击「连接」即可加入虚拟局域网";
            _lblSummary.Font = new Font("Microsoft YaHei UI", 10.5f);
            _lblSummary.ForeColor = _t.TextDim;
            _lblSummary.Location = new Point(26, 52);
            _lblSummary.AutoSize = true;
            statusCard.Controls.Add(_lblSummary);

            var lblCardHint = new Label();
            lblCardHint.Text = "小贴士：联机三要素 = 同一服务器 · 同一网段 · 不同后缀";
            lblCardHint.Font = new Font("Microsoft YaHei UI", 8.5f);
            lblCardHint.ForeColor = _t.TextFaint;
            lblCardHint.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            lblCardHint.AutoSize = true;
            lblCardHint.Location = new Point(26, statusCard.Height - 34);
            statusCard.Controls.Add(lblCardHint);

            var lblCardRight = new Label();
            lblCardRight.Text = "模式：点对点直连（自动）";
            lblCardRight.Font = new Font("Microsoft YaHei UI", 8.5f);
            lblCardRight.ForeColor = _t.TextFaint;
            lblCardRight.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            lblCardRight.AutoSize = true;
            lblCardRight.Location = new Point(statusCard.Width - 200, statusCard.Height - 34);
            statusCard.Controls.Add(lblCardRight);

            // 卡片② 配置虚拟 IP（Top）
            var card2 = new RoundedPanel();
            card2.Dock = DockStyle.Top;
            card2.Height = 152;
            card2.ShowBorder = true;
            page.Controls.Add(card2);

            var lblStep2 = new Label();
            lblStep2.Text = "② 配置虚拟 IP（加入虚拟局域网的身份）";
            lblStep2.Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold);
            lblStep2.Location = new Point(26, 16);
            lblStep2.AutoSize = true;
            card2.Controls.Add(lblStep2);

            _txtPrefix = new RoundedTextBox();
            _txtPrefix.Text = _cfg.NetworkPrefix;
            _txtPrefix.Location = new Point(26, 56);
            _txtPrefix.Size = new Size(64, 34);
            _txtPrefix.TextChangedInternal += (s, e) => _cfg.NetworkPrefix = _txtPrefix.Text.Trim();
            card2.Controls.Add(_txtPrefix);

            var lblDot1 = new Label();
            lblDot1.Text = ".";
            lblDot1.Font = new Font("Microsoft YaHei UI", 14f, FontStyle.Bold);
            lblDot1.Location = new Point(94, 60);
            lblDot1.AutoSize = true;
            card2.Controls.Add(lblDot1);

            _txtSegment = new RoundedTextBox();
            _txtSegment.Placeholder = "网段（所有人相同）";
            _txtSegment.Location = new Point(110, 56);
            _txtSegment.Size = new Size(150, 34);
            _txtSegment.TextChangedInternal += (s, e) => { int tv; if (int.TryParse(_txtSegment.Text.Trim(), out tv)) _cfg.Segment = _txtSegment.Text.Trim(); };
            card2.Controls.Add(_txtSegment);

            var lblDot2 = new Label();
            lblDot2.Text = ".";
            lblDot2.Font = new Font("Microsoft YaHei UI", 14f, FontStyle.Bold);
            lblDot2.Location = new Point(264, 60);
            lblDot2.AutoSize = true;
            card2.Controls.Add(lblDot2);

            _txtSuffix = new RoundedTextBox();
            _txtSuffix.Placeholder = "后缀（每人不同）";
            _txtSuffix.Location = new Point(280, 56);
            _txtSuffix.Size = new Size(150, 34);
            _txtSuffix.TextChangedInternal += (s, e) => { int tv; if (int.TryParse(_txtSuffix.Text.Trim(), out tv)) _cfg.Suffix = _txtSuffix.Text.Trim(); };
            card2.Controls.Add(_txtSuffix);

            var lblHint2 = new Label();
            lblHint2.Text = "提示：网段所有人填一样，后缀每人填不同（1~254）。例：10.10.30.100 / 10.10.30.101";
            lblHint2.Font = new Font("Microsoft YaHei UI", 8.5f);
            lblHint2.ForeColor = _t.TextFaint;
            lblHint2.Location = new Point(26, 104);
            lblHint2.AutoSize = true;
            card2.Controls.Add(lblHint2);

            // 卡片① 选择服务器（Top）
            var card1 = new RoundedPanel();
            card1.Dock = DockStyle.Top;
            card1.Height = 200;
            card1.ShowBorder = true;
            page.Controls.Add(card1);

            var lblStep1 = new Label();
            lblStep1.Text = "① 选择服务器（超级节点 / 中继）";
            lblStep1.Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold);
            lblStep1.Location = new Point(26, 16);
            lblStep1.AutoSize = true;
            card1.Controls.Add(lblStep1);

            _serverSeg = new SegmentedControl();
            _serverSeg.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            _serverSeg.Location = new Point(26, 52);
            _serverSeg.Size = new Size(card1.Width - 52, 132);
            card1.Controls.Add(_serverSeg);

            var pageOfficial = new Panel();
            var lblOff = new Label();
            lblOff.Text = "选择官方免费节点（无需搭建，适合快速联机）";
            lblOff.Font = new Font("Microsoft YaHei UI", 9f);
            lblOff.Location = new Point(0, 2);
            lblOff.AutoSize = true;
            pageOfficial.Controls.Add(lblOff);
            _cmbOfficial = new OdComboBox();
            _cmbOfficial.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            _cmbOfficial.Location = new Point(0, 26);
            _cmbOfficial.Size = new Size(470, 30);
            pageOfficial.Controls.Add(_cmbOfficial);
            _btnRefreshLatency = new FlatButton();
            _btnRefreshLatency.Text = "刷新延迟";
            _btnRefreshLatency.Kind = FlatButton.BtnKind.Ghost;
            _btnRefreshLatency.Location = new Point(480, 24);
            _btnRefreshLatency.Size = new Size(80, 30);
            _btnRefreshLatency.Click += (s, e) => RefreshServerLatencies();
            pageOfficial.Controls.Add(_btnRefreshLatency);
            pageOfficial.Resize += (s, e) =>
            {
                int w = pageOfficial.ClientSize.Width;
                _cmbOfficial.Width = Math.Max(120, w - 90);
                _btnRefreshLatency.Left = Math.Max(480, w - 86);
            };
            _serverSeg.AddPage("官方服务器", pageOfficial);

            var pageCustom = new Panel();
            var lblCus = new Label();
            lblCus.Text = "填好友或自己搭建的服务器地址（支持 playit.gg 等中继域名）";
            lblCus.Font = new Font("Microsoft YaHei UI", 9f);
            lblCus.Location = new Point(0, 2);
            lblCus.AutoSize = true;
            pageCustom.Controls.Add(lblCus);
            _txtCustom = new RoundedTextBox();
            _txtCustom.Placeholder = "例如 xxx.at.ply.gg 或 1.2.3.4";
            _txtCustom.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            _txtCustom.Location = new Point(0, 26);
            _txtCustom.Size = new Size(560, 34);
            pageCustom.Resize += (s, e) => { _txtCustom.Width = Math.Max(120, pageCustom.ClientSize.Width); };
            pageCustom.Controls.Add(_txtCustom);
            _serverSeg.AddPage("自定义服务器", pageCustom);

            // 底部操作条（Bottom）
            var bottom = new Panel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 72;
            page.Controls.Add(bottom);

            _btnConnect = new GButton();
            _btnConnect.Text = "连接";
            _btnConnect.Size = new Size(160, 44);
            _btnConnect.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnConnect.Location = new Point(bottom.Width - 186, 14);
            _btnConnect.Font = new Font("Microsoft YaHei UI", 12f, FontStyle.Bold);
            _btnConnect.Click += (s, e) => ToggleConnect();
            bottom.Controls.Add(_btnConnect);

            var lblBtm = new Label();
            lblBtm.Text = "底层 n2n · 自动打洞直连";
            lblBtm.Font = new Font("Microsoft YaHei UI", 8.5f);
            lblBtm.ForeColor = _t.TextFaint;
            lblBtm.Location = new Point(26, 28);
            lblBtm.AutoSize = true;
            bottom.Controls.Add(lblBtm);

            // 页头（最后添加 → 停靠最上）
            page.Controls.Add(MakeHeader("联机功能", "通过超级节点，把不同网络的电脑组成一个虚拟局域网"));

            return page;
        }

        // ==========================================================
        // 在线成员页        // ==========================================================
        private Panel BuildMembersPage()
        {
            var page = new Panel();

            var card = new RoundedPanel();
            card.Dock = DockStyle.Fill;
            card.ShowBorder = true;
            page.Controls.Add(card);

            _listMembers = new ListView();
            _listMembers.Dock = DockStyle.Fill;
            _listMembers.View = View.Details;
            _listMembers.FullRowSelect = true;
            _listMembers.GridLines = false;
            _listMembers.BorderStyle = BorderStyle.None;
            _listMembers.Font = new Font("Microsoft YaHei UI", 9.5f);
            _listMembers.OwnerDraw = true;
            _listMembers.DrawColumnHeader += (s, e) =>
            {
                var t = Theme.Current;
                using (var b = new SolidBrush(t.CardAlt)) e.Graphics.FillRectangle(b, e.Bounds);
                using (var b = new SolidBrush(t.TextDim))
                {
                    e.Graphics.DrawString(e.Header.Text, _fColHeader, b,
                        new PointF(e.Bounds.X + 8, e.Bounds.Y + 5));
                }
            };
            _listMembers.DrawSubItem += (s, e) =>
            {
                var t = Theme.Current;
                using (var b = new SolidBrush(e.Item.Selected ? Theme.AccentTint() : t.CardBg)) e.Graphics.FillRectangle(b, e.Bounds);
                using (var b = new SolidBrush(e.Item.Selected ? t.Accent1 : t.Text))
                {
                    e.Graphics.DrawString(e.SubItem.Text, _fColItem,
                        b, new PointF(e.Bounds.X + 8, e.Bounds.Y + 4));
                }
            };
            _listMembers.Columns.Add("虚拟 IP", 170);
            _listMembers.Columns.Add("状态", 200);
            _listMembers.Columns.Add("说明", 300);
            card.Controls.Add(_listMembers);

            var topRow = new Panel();
            topRow.Dock = DockStyle.Top;
            topRow.Height = 44;
            card.Controls.Add(topRow);

            _lblMembersInfo = new Label();
            _lblMembersInfo.Text = "连接后点击「刷新」扫描当前虚拟局域网中的在线成员";
            _lblMembersInfo.Font = new Font("Microsoft YaHei UI", 9f);
            _lblMembersInfo.ForeColor = _t.TextDim;
            _lblMembersInfo.Location = new Point(18, 13);
            _lblMembersInfo.AutoSize = true;
            topRow.Controls.Add(_lblMembersInfo);

            _btnRefresh = new FlatButton();
            _btnRefresh.Text = "刷新";
            _btnRefresh.Kind = FlatButton.BtnKind.Soft;
            _btnRefresh.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnRefresh.Location = new Point(card.Width - 96, 6);
            _btnRefresh.Size = new Size(80, 32);
            _btnRefresh.Click += (s, e) => RefreshMembers();

            _listMembers.MouseDoubleClick += (s, e) =>
            {
                var lv = (ListView)s;
                var info = lv.HitTest(e.Location);
                if (info.Item == null) return;
                string dip = info.Item.Text;
                if (info.Item.SubItems.Count > 1 && info.Item.SubItems[1].Text == "本机") return;
                if (string.IsNullOrEmpty(dip)) return;
                OpenMemberFiles(dip);
            };
            // 右键菜单：发送文件（P2P 高速传输）
            var memberMenu = new ContextMenuStrip();
            var miSendFile = new ToolStripMenuItem("发送文件（高速直传）");
            miSendFile.Click += (s, e) =>
            {
                if (_listMembers.SelectedItems.Count == 0) return;
                string dip = _listMembers.SelectedItems[0].Text;
                if (_listMembers.SelectedItems[0].SubItems.Count > 1 &&
                    _listMembers.SelectedItems[0].SubItems[1].Text == "本机") return;
                SendFileToMember(dip);
            };
            memberMenu.Items.Add(miSendFile);
            _listMembers.ContextMenuStrip = memberMenu;
            topRow.Controls.Add(_btnRefresh);

            _listMembers.Resize += (s, e) =>
            {
                if (_listMembers.Columns.Count == 3)
                    _listMembers.Columns[2].Width = Math.Max(200, _listMembers.Width - 378);
            };

            page.Controls.Add(MakeHeader("在线成员", "虚拟局域网中已发现的设备 · peer 每 3 秒查询，全段 Ping 每 20 秒扫描"));

            return page;
        }

        // ==========================================================
        // 网络测试页        // ==========================================================
        // ==========================================================
        // 双击在线成员 -> 探测对方文件共享端口 -> 内置浏览器跳转
        // ==========================================================
        private void OpenMemberFiles(string ip)
        {
            _lblMembersInfo.Text = "正在探测 " + ip + " 的文件共享端口…";
            var th = new System.Threading.Thread(() =>
            {
                int port = ProbeMemberPort(ip);
                if (port <= 0)
                {
                    UI(() =>
                    {
                        _lblMembersInfo.Text = "未在 " + ip + " 发现文件共享服务";
                        MessageBox.Show("未在 " + ip + " 上发现正在运行的文件共享（HFS）服务。\r\n已探测常用端口：8080、8000-8099、8888、9000。\r\n请确认对方已启动「文件共享」且双方虚拟局域网已连通。", "无法连接", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    });
                    return;
                }
                string url = string.Format("http://{0}:{1}", ip, port);
                UI(() =>
                {
                    if (_pageBrowser != null) _pageBrowser.Navigate(url);
                    ShowPage("browser");
                    _lblMembersInfo.Text = "已连接 " + ip + " 的文件共享（端口 " + port + "）";
                });
            });
            th.IsBackground = true;
            th.Start();
        }

        /// <summary>格式化文件大小</summary>
        private static string FormatSize(long bytes)
        {
            if (bytes < 1024) return bytes + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0.0") + " KB";
            if (bytes < 1024 * 1024 * 1024) return (bytes / 1024.0 / 1024.0).ToString("0.0") + " MB";
            return (bytes / 1024.0 / 1024.0 / 1024.0).ToString("0.0") + " GB";
        }

        /// <summary>发送文件给成员（P2P 高速直传，TCP 虚拟 IP 直连）</summary>
        private void SendFileToMember(string ip)
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "选择要发送的文件";
                if (dlg.ShowDialog(this) != DialogResult.OK) return;
                string filePath = dlg.FileName;
                string fileName = Path.GetFileName(filePath);
                long fileSize = new FileInfo(filePath).Length;

                // 传输进度对话框
                var progForm = new Form();
                progForm.Text = "正在发送文件";
                progForm.Size = new Size(420, 180);
                progForm.StartPosition = FormStartPosition.CenterParent;
                progForm.FormBorderStyle = FormBorderStyle.FixedDialog;
                progForm.MaximizeBox = false;
                progForm.MinimizeBox = false;
                var lblFile = new Label { Text = "文件: " + fileName, Location = new Point(15, 15), AutoSize = true };
                var lblSize = new Label { Text = "大小: " + FormatSize(fileSize), Location = new Point(15, 40), AutoSize = true };
                var lblStatus = new Label { Text = "正在连接 " + ip + ":8081 ...", Location = new Point(15, 65), AutoSize = true };
                var progBar = new ProgressBar { Location = new Point(15, 90), Width = 370, Height = 20, Style = ProgressBarStyle.Marquee };
                progForm.Controls.AddRange(new Control[] { lblFile, lblSize, lblStatus, progBar });

                var th = new System.Threading.Thread(() =>
                {
                    try
                    {
                        int port = _cfg.TransferPort > 0 ? _cfg.TransferPort : 8081;
                        bool ok = FileTransfer.SendFile(ip, port, filePath, 15000);
                        progForm.Invoke(new Action(() =>
                        {
                            progForm.Close();
                            if (ok)
                                MessageBox.Show(this, "文件已发送到 " + ip + "\n\n文件: " + fileName + "\n大小: " + FormatSize(fileSize) + "\n\n对方保存在其共享目录中。", "发送成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
                            else
                                MessageBox.Show(this, "发送失败：无法连接到 " + ip + ":8081\n\n请确认对方也在运行 NtoNTier 且已连接虚拟局域网。", "发送失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }));
                    }
                    catch (Exception ex)
                    {
                        progForm.Invoke(new Action(() =>
                        {
                            progForm.Close();
                            MessageBox.Show(this, "发送出错: " + ex.Message, "发送失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        }));
                    }
                });
                th.IsBackground = true;
                th.Start();
                progForm.ShowDialog(this);
            }
        }

        // 端口自适应探测: 8080 优先, 失败则并发探测 8000-8099 / 8888 / 9000
        private static int ProbeMemberPort(string ip)
        {
            if (TcpHttpOk(ip, 8080)) return 8080;
            var ports = new System.Collections.Generic.List<int>();
            for (int p = 8000; p <= 8099; p++) if (p != 8080) ports.Add(p);
            ports.Add(8888);
            ports.Add(9000);
            int found = 0;
            System.Threading.Tasks.Parallel.ForEach(ports,
                new System.Threading.Tasks.ParallelOptions { MaxDegreeOfParallelism = 24 },
                (p, state) =>
                {
                    if (found != 0) return;
                    if (TcpHttpOk(ip, p))
                    {
                        if (System.Threading.Interlocked.CompareExchange(ref found, p, 0) == 0)
                            state.Stop();
                    }
                });
            return found;
        }

        // TCP 连通 + HTTP 响应头验证
        private static bool TcpHttpOk(string ip, int port)
        {
            try
            {
                using (var tc = new System.Net.Sockets.TcpClient())
                {
                    var ar = tc.BeginConnect(ip, port, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(500))
                    {
                        try { tc.Close(); } catch { }
                        return false;
                    }
                    tc.EndConnect(ar);
                    using (var ns = tc.GetStream())
                    {
                        ns.ReadTimeout = 800;
                        ns.WriteTimeout = 800;
                        var req = System.Text.Encoding.ASCII.GetBytes("GET / HTTP/1.0\r\nHost: " + ip + "\r\n\r\n");
                        ns.Write(req, 0, req.Length);
                        var buf = new byte[64];
                        int n = ns.Read(buf, 0, buf.Length);
                        if (n <= 0) return false;
                        return System.Text.Encoding.ASCII.GetString(buf, 0, n).StartsWith("HTTP/");
                    }
                }
            }
            catch { return false; }
        }

        private Panel BuildTestPage()
        {
            var page = new Panel();

            var card = new RoundedPanel();
            card.Dock = DockStyle.Fill;
            card.ShowBorder = true;
            page.Controls.Add(card);

            _txtTestResult = new LogBox();
            _txtTestResult.Dock = DockStyle.Fill;
            card.Controls.Add(_txtTestResult);

            _testSeg = new SegmentedControl();
            _testSeg.Dock = DockStyle.Top;
            _testSeg.Height = 120;
            card.Controls.Add(_testSeg);

            // Ping 页
            var tp = new Panel();
            var l1 = new Label(); l1.Text = "目标 IP:"; l1.Location = new Point(0, 12); l1.AutoSize = true; tp.Controls.Add(l1);
            _txtPingHost = new RoundedTextBox(); _txtPingHost.Placeholder = "例如 10.10.30.101"; _txtPingHost.Location = new Point(62, 6); _txtPingHost.Size = new Size(210, 34); tp.Controls.Add(_txtPingHost);
            var b1 = new FlatButton(); b1.Text = "开始 Ping"; b1.Kind = FlatButton.BtnKind.Soft; b1.Location = new Point(288, 4); b1.Size = new Size(112, 38); b1.Click += (s, e) => RunTest("ping"); tp.Controls.Add(b1);
            var l1b = new Label(); l1b.Text = "共 4 次 · 超时 2s"; l1b.Font = new Font("Microsoft YaHei UI", 8.5f); l1b.ForeColor = _t.TextFaint; l1b.Location = new Point(414, 18); l1b.AutoSize = true; tp.Controls.Add(l1b);
            _testSeg.AddPage("Ping", tp);

            // TCP 页
            var tt = new Panel();
            var l2 = new Label(); l2.Text = "目标 IP:"; l2.Location = new Point(0, 12); l2.AutoSize = true; tt.Controls.Add(l2);
            _txtTcpHost = new RoundedTextBox(); _txtTcpHost.Placeholder = "例如 10.10.30.101"; _txtTcpHost.Location = new Point(62, 6); _txtTcpHost.Size = new Size(180, 34); tt.Controls.Add(_txtTcpHost);
            var l2b = new Label(); l2b.Text = "端口:"; l2b.Location = new Point(254, 12); l2b.AutoSize = true; tt.Controls.Add(l2b);
            _txtTcpPort = new RoundedTextBox(); _txtTcpPort.Placeholder = "3301"; _txtTcpPort.Location = new Point(292, 6); _txtTcpPort.Size = new Size(80, 34); tt.Controls.Add(_txtTcpPort);
            var b2 = new FlatButton(); b2.Text = "测试 TCP"; b2.Kind = FlatButton.BtnKind.Soft; b2.Location = new Point(388, 4); b2.Size = new Size(112, 38); b2.Click += (s, e) => RunTest("tcp"); tt.Controls.Add(b2);
            _testSeg.AddPage("TCP 端口", tt);

            // UDP 页
            var tu = new Panel();
            var l3 = new Label(); l3.Text = "目标 IP:"; l3.Location = new Point(0, 12); l3.AutoSize = true; tu.Controls.Add(l3);
            _txtUdpHost = new RoundedTextBox(); _txtUdpHost.Placeholder = "例如 10.10.30.101"; _txtUdpHost.Location = new Point(62, 6); _txtUdpHost.Size = new Size(180, 34); tu.Controls.Add(_txtUdpHost);
            var l3b = new Label(); l3b.Text = "端口:"; l3b.Location = new Point(254, 12); l3b.AutoSize = true; tu.Controls.Add(l3b);
            _txtUdpPort = new RoundedTextBox(); _txtUdpPort.Placeholder = "3301"; _txtUdpPort.Location = new Point(292, 6); _txtUdpPort.Size = new Size(80, 34); tu.Controls.Add(_txtUdpPort);
            var b3 = new FlatButton(); b3.Text = "发送 UDP"; b3.Kind = FlatButton.BtnKind.Soft; b3.Location = new Point(388, 4); b3.Size = new Size(112, 38); b3.Click += (s, e) => RunTest("udp"); tu.Controls.Add(b3);
            _testSeg.AddPage("UDP 探测", tu);

            
            page.Controls.Add(MakeHeader("网络测试", "验证虚拟局域网是否打通，判断直连 / 中继延迟"));

            return page;
        }

        // ==========================================================
        // 文件共享页 (HFS)        // ==========================================================
        private Panel BuildFilesharePage()
        {
            var page = new Panel();

            var card = new RoundedPanel();
            card.Dock = DockStyle.Fill;
            card.ShowBorder = true;
            page.Controls.Add(card);

            var scroll = new Panel();
            scroll.Dock = DockStyle.Fill;
            scroll.AutoScroll = true;
            card.Controls.Add(scroll);

            int x = 26;
            int y = 16;

            // 状态卡
            var box = new RoundedPanel();
            box.Size = new Size(520, 74);
            box.Location = new Point(x, y);
            box.ShowBorder = true;
            scroll.Controls.Add(box);
            var l0 = new Label();
            l0.Text = "共享状态";
            l0.Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
            l0.ForeColor = _t.TextDim;
            l0.Location = new Point(16, 12);
            l0.AutoSize = true;
            box.Controls.Add(l0);
            _lblHfsStatus = new Label();
            _lblHfsStatus.Text = "未启动";
            _lblHfsStatus.Font = new Font("Microsoft YaHei UI", 10.5f);
            _lblHfsStatus.ForeColor = _t.TextFaint;
            _lblHfsStatus.Location = new Point(16, 40);
            _lblHfsStatus.AutoSize = true;
            box.Controls.Add(_lblHfsStatus);
            y += 92;

            // 端口
            var lp = new Label();
            lp.Text = "端口:";
            lp.Location = new Point(x, y + 8);
            lp.AutoSize = true;
            scroll.Controls.Add(lp);
            _txtHfsPort = new RoundedTextBox();
            _txtHfsPort.Placeholder = "8080";
            _txtHfsPort.Location = new Point(x + 46, y);
            _txtHfsPort.Size = new Size(90, 34);
            scroll.Controls.Add(_txtHfsPort);
            var lp2 = new Label();
            lp2.Text = "局域网成员用浏览器访问 http://虚拟IP:端口";
            lp2.Font = new Font("Microsoft YaHei UI", 8.5f);
            lp2.ForeColor = _t.TextFaint;
            lp2.Location = new Point(x + 148, y + 9);
            lp2.AutoSize = true;
            scroll.Controls.Add(lp2);
            y += 52;

            // 共享目录
            var ld = new Label();
            ld.Text = "共享目录:";
            ld.Location = new Point(x, y + 8);
            ld.AutoSize = true;
            scroll.Controls.Add(ld);
            _txtHfsDir = new RoundedTextBox();
            _txtHfsDir.Placeholder = "选择要共享的文件夹（留空默认 文档/NtoNTier共享）";
            _txtHfsDir.Location = new Point(x + 72, y);
            _txtHfsDir.Size = new Size(330, 34);
            scroll.Controls.Add(_txtHfsDir);
            var bdir = new FlatButton();
            bdir.Text = "浏览...";
            bdir.Kind = FlatButton.BtnKind.Soft;
            bdir.Location = new Point(x + 414, y - 2);
            bdir.Size = new Size(80, 38);
            bdir.Click += (s, e) =>
            {
                using (var dlg = new FolderBrowserDialog())
                {
                    dlg.Description = "选择要共享的文件夹";
                    if (Directory.Exists(_txtHfsDir.Text)) dlg.SelectedPath = _txtHfsDir.Text;
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                    {
                        _txtHfsDir.Text = dlg.SelectedPath;
                    }
                }
            };
            scroll.Controls.Add(bdir);
            y += 52;

            // 访问地址
            var la = new Label();
            la.Text = "访问地址:";
            la.Location = new Point(x, y + 8);
            la.AutoSize = true;
            scroll.Controls.Add(la);
            _lblHfsUrl = new Label();
            _lblHfsUrl.Text = "http://虚拟IP:8080";
            _lblHfsUrl.Font = new Font("Microsoft YaHei UI", 10f, FontStyle.Bold);
            _lblHfsUrl.ForeColor = _t.Accent1;
            _lblHfsUrl.Location = new Point(x + 72, y + 6);
            _lblHfsUrl.AutoSize = true;
            scroll.Controls.Add(_lblHfsUrl);
            var bopen = new FlatButton();
            bopen.Text = "打开文件浏览器";
            bopen.Kind = FlatButton.BtnKind.Soft;
            bopen.Location = new Point(x + 320, y);
            bopen.Size = new Size(120, 38);
            bopen.Click += (s, e) =>
            {
                string url = GetHfsUrl();
                if (string.IsNullOrEmpty(url) || url.Contains("虚拟IP")) { MessageBox.Show("请先连接，并确认端口正确", "提示"); return; }
                if (_pageBrowser != null) _pageBrowser.Navigate(url);
                ShowPage("browser");
            };
            scroll.Controls.Add(bopen);
            y += 58;

            // 操作按钮
            _btnHfsStart = new FlatButton();
            _btnHfsStart.Text = "启动文件共享";
            _btnHfsStart.Kind = FlatButton.BtnKind.Soft;
            _btnHfsStart.Location = new Point(x, y);
            _btnHfsStart.Size = new Size(150, 42);
            _btnHfsStart.Click += (s, e) => StartHfs();
            scroll.Controls.Add(_btnHfsStart);
            _btnHfsStop = new FlatButton();
            _btnHfsStop.Text = "停止";
            _btnHfsStop.Kind = FlatButton.BtnKind.Ghost;
            _btnHfsStop.Location = new Point(x + 168, y);
            _btnHfsStop.Size = new Size(90, 42);
            _btnHfsStop.Click += (s, e) =>
            {
                string r = HfsManager.Stop();
                SaveHfsUi();
                RefreshHfsStatus();
                MessageBox.Show(r, "文件共享");
            };
            scroll.Controls.Add(_btnHfsStop);
            y += 62;

            // 提示
            var hint = new Label();
            hint.Text = "说明：启动后，虚拟局域网内的朋友用浏览器打开上面的访问地址，即可浏览、下载你共享的文件夹。\r\n本机可在浏览器地址栏打开 http://localhost:端口 进入管理端，输入管理员账号可上传/管理文件。";
            hint.Font = new Font("Microsoft YaHei UI", 8.5f);
            hint.ForeColor = _t.TextFaint;
            hint.Location = new Point(x, y);
            hint.AutoSize = true;
            scroll.Controls.Add(hint);

            // 载入配置
            _txtHfsPort.Text = _cfg.HfsPort.ToString();
            if (!string.IsNullOrEmpty(_cfg.HfsShareDir)) _txtHfsDir.Text = _cfg.HfsShareDir;
            _lblHfsUrl.Text = GetHfsUrl();
            RefreshHfsStatus();

            page.Controls.Add(MakeHeader("文件共享", "把文件夹共享给虚拟局域网内的所有成员"));
            return page;
        }

        /// <summary>当前虚拟 IP（未连接时返回占位）</summary>
        private string GetMyVip()
        {
            try
            {
                if (_cfg.AutoIP)
                {
                    string tapIp = Net.GetTapIp(_cfg.TapName);
                    if (!string.IsNullOrEmpty(tapIp)) return tapIp;
                }
                else
                {
                    return string.Format("{0}.{1}.{2}", _cfg.NetworkPrefix, _cfg.Segment, _cfg.Suffix);
                }
            }
            catch { }
            return "虚拟IP";
        }

        private int ParseHfsPort()
        {
            int p;
            if (int.TryParse(_txtHfsPort.Text.Trim(), out p) && p > 0 && p <= 65535) return p;
            return 8080;
        }

        private string GetHfsUrl()
        {
            string ip = GetMyVip();
            if (ip == "虚拟IP" || ip == "(自动获取)" || ip == "自动获取") return "http://虚拟IP:" + ParseHfsPort();
            return string.Format("http://{0}:{1}", ip, ParseHfsPort());
        }

        private void SaveHfsUi()
        {
            _cfg.HfsPort = ParseHfsPort();
            _cfg.HfsShareDir = _txtHfsDir.Text.Trim();
            _cfg.Save();
        }

        private void StartHfs()
        {
            int port = ParseHfsPort();
            string dir = _txtHfsDir.Text.Trim();
            if (string.IsNullOrEmpty(dir))
            {
                dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NtoNTier共享");
                _txtHfsDir.Text = dir;
            }
            if (string.IsNullOrEmpty(_cfg.HfsPassword))
            {
                _cfg.HfsPassword = GenerateHfsPassword(8);
            }
            string pwd = _cfg.HfsPassword;
            _btnHfsStart.Enabled = false;
            _lblHfsStatus.Text = "正在启动文件共享...";
            // HfsManager.Start 内部有端口轮询（最多5秒），放后台线程避免阻塞UI
            var t = new System.Threading.Thread(delegate()
            {
                string r = HfsManager.Start(port, dir, pwd);
                UI(delegate()
                {
                    SaveHfsUi();
                    RefreshHfsStatus();
                    _lblHfsUrl.Text = GetHfsUrl();
                    _btnHfsStart.Enabled = true;
                    MessageBox.Show(r + (r.StartsWith("启动") ? "\r\n\r\n管理员账号：admin\r\n管理员密码：" + pwd : ""), "文件共享");
                });
            });
            t.IsBackground = true;
            t.Start();
        }

        private string GenerateHfsPassword(int len)
        {
            const string chars = "abcdefghjkmnpqrstuvwxyzABCDEFGHJKMNPQRSTUVWXYZ23456789";
            var rnd = new Random();
            var sb = new StringBuilder();
            for (int i = 0; i < len; i++) sb.Append(chars[rnd.Next(chars.Length)]);
            return sb.ToString();
        }

        private void RefreshHfsStatus()
        {
            bool running = HfsManager.IsHfsRunning;
            if (running)
            {
                _lblHfsStatus.Text = "运行中 · PID " + HfsManager.HfsPid + " · 端口 " + ParseHfsPort();
                _lblHfsStatus.ForeColor = _t.Success;
                _btnHfsStart.Text = "重新启动";
            }
            else
            {
                _lblHfsStatus.Text = "未启动 · 点「启动文件共享」即可开始";
                _lblHfsStatus.ForeColor = _t.TextFaint;
                _btnHfsStart.Text = "启动文件共享";
            }
        }

        // ==========================================================
        // 文件浏览页（内置 WebView2 浏览器 + 分片多线程下载）
        // ==========================================================
        private BrowserPage BuildBrowserPage()
        {
            var bp = new BrowserPage();
            bp.DownloadFinished += task =>
            {
                try
                {
                    string msg = "下载完成：" + task.FileName + "\r\n已保存到 " + task.SavePath;
                    if (InvokeRequired)
                        BeginInvoke(new Action(() => MessageBox.Show(msg, "下载完成")));
                    else
                        MessageBox.Show(msg, "下载完成");
                }
                catch { }
            };
            return bp;
        }


        // ==========================================================
        // 使用指南页        // ==========================================================
        private Panel BuildGuidePage()
        {
            var page = new Panel();

            var card = new RoundedPanel();
            card.Dock = DockStyle.Fill;
            card.ShowBorder = true;
            page.Controls.Add(card);

            var scroll = new Panel();
            scroll.Dock = DockStyle.Fill;
            scroll.AutoScroll = true;
            card.Controls.Add(scroll);

            int y = 10;
            y = GuideSection(scroll, y, "目标",
                "把几台在不同网络（甚至不同城市）的电脑，组成一个\"虚拟局域网\"，\r\n让只支持局域网联机的游戏能互相搜到、一起玩。");
            y = GuideSection(scroll, y, "快速上手",
                "1. 所有要联机的人，都打开 NtoNTier。\r\n2. ① 选择服务器：官方服务器 = 免搭建直接选；自定义 = 填房主提供的中继地址（如 playit.gg）。\r\n3. ② 配置虚拟 IP：网段所有人填一样（如 30），后缀每人填不同（如 100 / 101）。\r\n4. 点「连接」，第一次会提示安装虚拟网卡驱动（TAP，已内置在程序里）。\r\n5. 看「在线成员」：大家都出现，说明虚拟局域网已经通了。\r\n6. 进游戏选\"局域网联机\"，就能互相看到主机了。");
            y = GuideSection(scroll, y, "常见问题",
                "· 连不上 / 看不到人：先用「网络测试」Ping 对方虚拟 IP；确认网段一致、后缀不重复。\r\n· 第一次连接失败：点「更多 → 安装TAP驱动」装好网卡再连。\r\n· 想要长期稳定：房主可搭自己的中继（见服务端包），地址填到「自定义服务器」");
            GuideSection(scroll, y, "原理",
                "底层使用开源 n2n（GPL-3.0），NtoNTier 是它的图形化外壳：\r\n先通过 supernode 协调打洞，能直连就直连（延迟最低），打不通自动走中继。");

            page.Controls.Add(MakeHeader("使用指南", "三分钟上手 · 小白友好"));

            return page;
        }

        private int GuideSection(Panel scroll, int y, string heading, string body)
        {
            var h = new Label();
            h.Text = heading;
            h.Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold);
            h.Location = new Point(10, y);
            h.AutoSize = true;
            scroll.Controls.Add(h);
            y += 28;
            var b = new Label();
            b.Text = body;
            b.Font = new Font("Microsoft YaHei UI", 9.5f);
            b.ForeColor = _t.TextDim;
            b.Location = new Point(10, y);
            b.AutoSize = true;
            b.MaximumSize = new Size(700, 0);
            scroll.Controls.Add(b);
            return y + b.PreferredHeight + 26;
        }

        // ==========================================================
        // 首次引导 + 驱动检测        // ==========================================================
        private void FirstRunFlow()
        {
            // 首启引导已按用户要求移除：首次启动直接标记完成，只做必要的 TAP 驱动检查
            if (_cfg.Onboarded || ShotMode) return;
            _cfg.Onboarded = true;
            _cfg.Save();
            CheckFirstRunDriver();
        }

        private void CheckFirstRunDriver()
        {
            if (!Net.TapExists(""))
            {
                var r = MessageBox.Show(this,
                    "首次使用需要安装\"虚拟网卡驱动\"（TAP），否则无法创建虚拟网卡。\n\n现在安装吗？（驱动已内置，点击后自动完成",
                    "NtoNTier · 安装虚拟网卡驱动",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (r == DialogResult.Yes)
                {
                    string msg = Net.InstallTap();
                    MessageBox.Show(this, msg, "安装结果", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        // ==========================================================
        // 逻辑
        // ==========================================================
        private void BuildOfficialList()
        {
            _cmbOfficial.Items.Clear();
            if (!string.IsNullOrEmpty(_cfg.OfficialServers))
            {
                foreach (string line in _cfg.OfficialServers.Split('\n'))
                {
                    string s = line.Trim();
                    if (s.Length > 0) _cmbOfficial.Items.Add(s);
                }
            }
            if (_cmbOfficial.Items.Count == 0) _cmbOfficial.Items.Add("免费服务器=n2n.lucktu.com:10088");
            _cmbOfficial.SelectedIndex = 0;
        }

        /// <summary>测试单个服务器的 ICMP 延迟，返回毫秒数，超时返回 -1</summary>
        private int TestServerLatency(string hostPort)
        {
            try
            {
                string host = hostPort;
                int colon = hostPort.LastIndexOf(':');
                if (colon > 0) host = hostPort.Substring(0, colon);
                using (var ping = new System.Net.NetworkInformation.Ping())
                {
                    var reply = ping.Send(host, 3000);
                    if (reply.Status == System.Net.NetworkInformation.IPStatus.Success)
                        return (int)reply.RoundtripTime;
                }
            }
            catch { }
            return -1;
        }

        /// <summary>后台测试所有官方服务器延迟，按延迟从低到高排序并更新下拉框</summary>
        private void RefreshServerLatencies()
        {
            if (_btnRefreshLatency != null)
            {
                _btnRefreshLatency.Enabled = false;
                _btnRefreshLatency.Text = "测试中...";
            }
            var thread = new System.Threading.Thread(() =>
            {
                var servers = new System.Collections.Generic.List<string[]>();
                if (!string.IsNullOrEmpty(_cfg.OfficialServers))
                {
                    foreach (string line in _cfg.OfficialServers.Split('\n'))
                    {
                        string s = line.Trim();
                        if (s.Length == 0) continue;
                        int eq = s.IndexOf('=');
                        string name = eq > 0 ? s.Substring(0, eq).Trim() : s;
                        string addr = eq > 0 ? s.Substring(eq + 1).Trim() : s;
                        servers.Add(new string[] { name, addr });
                    }
                }
                var results = new System.Collections.Generic.List<object[]>();
                foreach (var s in servers)
                {
                    int lat = TestServerLatency(s[1]);
                    results.Add(new object[] { s[0], s[1], lat });
                }
                results.Sort((a, b) =>
                {
                    int la = (int)a[2], lb = (int)b[2];
                    if (la < 0 && lb < 0) return 0;
                    if (la < 0) return 1;
                    if (lb < 0) return -1;
                    return la.CompareTo(lb);
                });
                UI(() =>
                {
                    _cmbOfficial.Items.Clear();
                    foreach (var r in results)
                    {
                        int lat = (int)r[2];
                        string latStr = lat >= 0 ? lat + "ms" : "超时";
                        _cmbOfficial.Items.Add(r[0] + " (" + latStr + ")=" + r[1]);
                    }
                    if (_cmbOfficial.Items.Count > 0) _cmbOfficial.SelectedIndex = 0;
                    if (_btnRefreshLatency != null)
                    {
                        _btnRefreshLatency.Enabled = true;
                        _btnRefreshLatency.Text = "刷新延迟";
                    }
                });
            });
            thread.IsBackground = true;
            thread.Start();
        }

        private void LoadUiFromConfig()
        {
            _txtSegment.Text = _cfg.Segment;
            _txtSuffix.Text = _cfg.Suffix;
            _txtCustom.Text = _cfg.CustomServer;
            if (_txtPrefix != null) _txtPrefix.Text = _cfg.NetworkPrefix;
            if (!string.IsNullOrEmpty(_cfg.Segment)) _txtSegment.Text = _cfg.Segment;
            if (!string.IsNullOrEmpty(_cfg.Suffix)) _txtSuffix.Text = _cfg.Suffix;
            UpdateAutoIpState();
        }

        private void UpdateAutoIpState()
        {
            bool en = !_cfg.AutoIP;
            _txtSegment.Enabled = en;
            _txtSuffix.Enabled = en;
            _txtSegment.Placeholder = en ? "网段（所有人相同）" : "已启用自动获取";
            _txtSuffix.Placeholder = en ? "后缀（每人不同）" : "已启用自动获取";
        }

        private void ToggleTheme()
        {
            bool dark = !_t.IsDark;
            Theme.Switch(this, dark);
            ApplyTheme();
            _btnTheme.Text = dark ? "暗色模式" : "亮色模式";
            _cfg.Dark = dark;
            _cfg.Save();
        }

        public void ToggleThemePublic()
        {
            try { ToggleTheme(); } catch { }
        }

        public void SelectServerCustom()
        {
            try { _serverSeg.SelectIndex(1); } catch { }
        }

        private void ApplyTheme()
        {
            BackColor = _t.WindowBg;
            _sidebar.BackColor = _t.SidebarBg;
            if (_sidebarFooter != null) _sidebarFooter.BackColor = _t.SidebarBg;
            _content.BackColor = _t.WindowBg;
            _lblVersion.ForeColor = _t.TextFaint;
            _lblStatusHint.ForeColor = _t.TextFaint;
            Theme.Apply(_content);
            foreach (Control c in _sidebar.Controls) { if (c is NavItem) ((NavItem)c).Invalidate(); }
            _btnTheme.Fill = _t.SidebarBg;
            _btnTheme.Invalidate();
            RefreshStatus(false);
        }

        private string CurrentServer()
        {
            if (_serverSeg.SelectedIndex == 1)
            {
                string v = SanitizeServer(_txtCustom.Text);
                if (v.Length == 0) return "";
                return v;
            }
            object o = _cmbOfficial.SelectedItem;
            if (o == null) return "";
            string item = o.ToString();
            int idx = item.IndexOf('=');
            if (idx >= 0) return item.Substring(idx + 1).Trim();
            return item.Trim();
        }


        /// <summary>清洗服务器地址：剥离 http:// https:// 前缀与尾部斜杠/路径。
        /// 防止用户误填成网址格式导致 edge 无法连接。</summary>
        private string SanitizeServer(string v)
        {
            if (v == null) return "";
            v = v.Trim();
            string lower = v.ToLowerInvariant();
            if (lower.StartsWith("http://")) v = v.Substring(7);
            else if (lower.StartsWith("https://")) v = v.Substring(8);
            v = v.TrimEnd('/');
            int slash = v.IndexOf('/');
            if (slash >= 0) v = v.Substring(0, slash);
            return v.Trim();
        }

        private void ToggleConnect()
        {
            if (Net.IsEdgeRunning)
            {
                Net.StopEdge();
                _btnConnect.Text = "连接";
                SetStatus(false, "未连接");
                _listMembers.Items.Clear();
                _lastScanIps.Clear();
                _supernodeWarned = false;
                return;
            }
            DoConnect();
        }

        private void DoConnect()
        {
            string server = CurrentServer();
            string rawCustom = _txtCustom.Text.Trim();
            if (_serverSeg.SelectedIndex == 1 && rawCustom.Length > 0
                && SanitizeServer(rawCustom) != rawCustom)
            {
                MessageBox.Show("已自动修正服务器地址为 " + server + "\n（服务器地址请直接填 IP:端口，不要加 http://）",
                    "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            if (server.Length == 0)
            {
                MessageBox.Show("请先选择或填写服务器地址。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (!_cfg.AutoIP)
            {
                int seg, suf;
                bool okS = int.TryParse(_txtSegment.Text.Trim(), out seg) && seg >= 0 && seg <= 255;
                bool okF = int.TryParse(_txtSuffix.Text.Trim(), out suf) && suf >= 1 && suf <= 254;
                if (!okS || !okF)
                {
                    MessageBox.Show("虚拟 IP 格式有误：网段填 0~255，后缀填 1~254。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                _cfg.Segment = seg.ToString();
                _cfg.Suffix = suf.ToString();
            }
            _cfg.CustomServer = SanitizeServer(_txtCustom.Text);
            _cfg.Save();

            if (!Net.TapExists(_cfg.TapName))
            {
                var r = MessageBox.Show(this,
                    "尚未找到虚拟网卡（TAP），需要先安装驱动才能连接。\n\n是否现在安装？",
                    "需要虚拟网卡驱动", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (r == DialogResult.Yes)
                {
                    MessageBox.Show(this, Net.InstallTap(), "安装结果", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                else return;
            }

            string result = Net.StartEdge(_cfg, server);
            if (result.StartsWith("ok"))
            {
                _btnConnect.Text = "断开";
                _supernodeWarned = false;
                SetStatus(true, "已连接· 正在验证 supernode...");
                // 后台验证 supernode 可达性（5秒后检查，给 edge 留注册时间）
                var verifyThread = new System.Threading.Thread(delegate()
                {
                    System.Threading.Thread.Sleep(5000);
                    bool ok = Net.QuerySupernodeReachable(_cfg.MgmtPort);
                    if (!ok)
                    {
                        UI(delegate()
                        {
                            if (!Net.IsEdgeRunning) return;
                            _statusDot.Set("supernode 未响应", Color.FromArgb(250, 173, 20));
                            _lblSummary.Text = "已连接· 服务器 " + server + " · supernode 未响应（可能无法发现其他成员，请检查服务器地址或换用自建 supernode）";
                            _lblSummary.ForeColor = Color.FromArgb(250, 173, 20);
                            if (!_supernodeWarned)
                            {
                                _supernodeWarned = true;
                                MessageBox.Show(this, "supernode (" + server + ") 未响应\n\n可能原因：\n1. 公共 supernode 服务已下线（lucktu 等免费节点不稳定）\n2. 本地网络封锁了 UDP 端口\n3. 服务器地址填写错误\n\n建议：使用自建 supernode（项目 dist\\server\\supernode.exe），地址填本机 IP:3301。", "supernode 连接警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                            }
                        });
                    }
                    else
                    {
                        UI(delegate()
                        {
                            if (!Net.IsEdgeRunning) return;
                            SetStatus(true, "已连接· supernode 已注册");
                        });
                    }
                });
                verifyThread.IsBackground = true;
                verifyThread.Start();
                string vip;
                if (_cfg.AutoIP)
                {
                    string tapIp = Net.GetTapIp(_cfg.TapName);
                    vip = string.IsNullOrEmpty(tapIp) ? "自动获取" : tapIp;
                }
                else vip = string.Format("{0}.{1}.{2}", _cfg.NetworkPrefix, _cfg.Segment, _cfg.Suffix);
                _lblSummary.Text = "已连接· 服务器 " + server + " · 虚拟 IP " + vip + " · 正在验证 supernode...";
                _lblSummary.ForeColor = _t.TextDim;
                if (!Net.TapExists(_cfg.TapName))
                {
                    SetStatus(true, "已连接（提示：若看不到网卡，请到「更多 → 安装TAP」）");
                }
                RefreshMembers();
                // 启动 P2P 文件传输服务（虚拟 IP TCP 直连，多线程分片）
                try
                {
                    int tPort = _cfg.TransferPort > 0 ? _cfg.TransferPort : 8081;
                    string tDir = string.IsNullOrEmpty(_cfg.HfsShareDir) ? null : _cfg.HfsShareDir;
                    FileTransfer.StartServer(tPort, tDir);
                }
                catch { }
            }
            else
            {
                SetStatus(false, "连接失败");
                MessageBox.Show(result, "连接失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void SetStatus(bool online, string text)
        {
            if (online)
            {
                _statusDot.Set(text, _t.Success);
            }
            else
            {
                _statusDot.Set(text, _t.TextFaint);
            }
        }

        private void RefreshStatus(bool force)
        {
            bool running = Net.IsEdgeRunning;
            _refreshCounter++;
            // 每 3 轮（约9秒）后台检测一次 supernode 可达性，掉线及时提示
            if (running && _refreshCounter % 3 == 0)
            {
                int mgmt = _cfg.MgmtPort;
                System.Threading.Tasks.Task.Run(delegate()
                {
                    bool ok = Net.QuerySupernodeReachable(mgmt);
                    if (IsDisposed) return;
                    try
                    {
                        BeginInvoke(new Action(delegate()
                        {
                            if (!Net.IsEdgeRunning) return;
                            if (!ok)
                            {
                                // supernode 掉线：状态点变黄，摘要同步更新
                                _supernodeWarned = true;
                                _statusDot.Set("supernode 掉线·正在重连", Color.FromArgb(250, 173, 20));
                                if (_lblSummary != null)
                                {
                                    _lblSummary.Text = "已连接· supernode 未响应（服务器可能重启或网络中断，edge 正在自动重连）";
                                    _lblSummary.ForeColor = Color.FromArgb(250, 173, 20);
                                }
                            }
                            else if (_supernodeWarned)
                            {
                                // 从掉线恢复：状态点变绿，摘要恢复
                                _supernodeWarned = false;
                                SetStatus(true, "已连接· supernode 已恢复");
                                if (_lblSummary != null)
                                {
                                    string vip;
                                    if (_cfg.AutoIP)
                                    {
                                        string tapIp = Net.GetTapIp(_cfg.TapName);
                                        vip = string.IsNullOrEmpty(tapIp) ? "自动获取" : tapIp;
                                    }
                                    else vip = string.Format("{0}.{1}.{2}", _cfg.NetworkPrefix, _cfg.Segment, _cfg.Suffix);
                                    _lblSummary.Text = "已连接· 服务器 " + CurrentServer() + " · 虚拟 IP " + vip;
                                    _lblSummary.ForeColor = _t.TextDim;
                                }
                            }
                        }));
                    }
                    catch { }
                });
            }
            if (running)
            {
                if (_btnConnect.Text != "断开")
                {
                    _btnConnect.Text = "断开";
                    SetStatus(true, "已连接(PID: " + Net.EdgePid + ")");
                }
                // 在线成员改为手动刷新（点击"刷新"按钮），不再每3秒自动扫描
                UpdateLinkMode();
            }
            else
            {
                if (_btnConnect.Text != "连接")
                {
                    _btnConnect.Text = "连接";
                    SetStatus(false, "未连接");
                    _lblSummary.Text = "尚未连接 · 选择服务器并点击「连接」即可加入虚拟局域网";
                    _lblSummary.ForeColor = _t.TextDim;
                    if (_lblLinkMode != null) _lblLinkMode.Text = "";
                    try { FileTransfer.StopServer(); } catch { }
                }
            }
        }

        private int _linkModeCounter = 0;
        private void UpdateLinkMode()
        {
            // 节流：链接模式 9 秒查询一次即可（每 3 轮 statusTimer），避免高频 UDP 查询
            if (++_linkModeCounter % 3 != 0) return;
            int port = _cfg.MgmtPort;
            if (IsDisposed) return;
            System.Threading.Tasks.Task.Run(delegate()
            {
                Net.LinkStatus st = Net.QueryLinkStatus(port);
                if (IsDisposed) return;
                try
                {
                    BeginInvoke(new Action(delegate() { ApplyLinkMode(st); }));
                }
                catch { }
            });
        }

        private void ApplyLinkMode(Net.LinkStatus st)
        {
            if (st == null || !st.QueryOk) { _lblLinkMode.Text = ""; return; }
            if (st.P2pPeers > 0 && st.RelayPeers == 0)
            {
                _lblLinkMode.Text = "● 打洞直连";
                _lblLinkMode.ForeColor = _t.Success;
            }
            else if (st.RelayPeers > 0)
            {
                _lblLinkMode.Text = st.P2pPeers > 0 ? "● 直连 + 中继" : "● 服务器中继";
                _lblLinkMode.ForeColor = Color.FromArgb(214, 158, 46);
            }
            else
            {
                _lblLinkMode.Text = "● 暂无流量";
                _lblLinkMode.ForeColor = _t.TextFaint;
            }
        }

        private void RefreshMembers()
        {
            if (!Net.IsEdgeRunning)
            {
                _listMembers.Items.Clear();
                _lblMembersInfo.Text = "尚未连接。连接后会自动扫描在线成员。";
                return;
            }
            _lblMembersInfo.Text = "虚拟局域网在线成员（点击「刷新」重新扫描）";
            // 本机虚拟 IP：手动模式用首页配置拼接；自动获取模式从 TAP 网卡读取实际分配值
            string localIp;
            string subnetPrefix;
            if (_cfg.AutoIP)
            {
                string tapIp = Net.GetTapIp(_cfg.TapName);
                localIp = string.IsNullOrEmpty(tapIp) ? "(自动获取)" : tapIp;
                subnetPrefix = string.IsNullOrEmpty(tapIp) ? "" : tapIp.Substring(0, tapIp.LastIndexOf('.') + 1);
            }
            else
            {
                localIp = string.Format("{0}.{1}.{2}", _cfg.NetworkPrefix, _cfg.Segment, _cfg.Suffix);
                subnetPrefix = string.Format("{0}.{1}.", _cfg.NetworkPrefix, _cfg.Segment);
            }
            // 增量构建目标行集合（不清空重建，避免列表闪烁与每 3 秒全量重绘卡顿）
            var rows = new List<ListViewItem>();
            var lvSelf = new ListViewItem(localIp);
            lvSelf.SubItems.Add("本机");
            lvSelf.SubItems.Add("当前电脑");
            rows.Add(lvSelf);
            // 在线成员（只取虚拟局域网网段的 IP）—— edge peer 查询在后台执行，避免管理端口超时卡 UI
            if (_queryInProgress)
            {
                // 上一次查询还没完成，直接用缓存结果，避免线程堆积
                ApplyMemberRows(rows, localIp);
            }
            else
            {
                _queryInProgress = true;
                var queryThread = new System.Threading.Thread(delegate()
                {
                    try
                    {
                        var ips = Net.QueryEdgeStatus(_cfg.MgmtPort, subnetPrefix);
                        var list = new List<string>();
                        foreach (string ip in ips)
                        {
                            if (ip == localIp) continue;
                            if (!list.Contains(ip)) list.Add(ip);
                        }
                        UI(delegate()
                        {
                            foreach (string ip in list)
                            {
                                bool exists = false;
                                foreach (ListViewItem r in rows) if (r.Text == ip) { exists = true; break; }
                                if (!exists)
                                {
                                    var lv = new ListViewItem(ip);
                                    lv.SubItems.Add("在线");
                                    lv.SubItems.Add("已通过虚拟局域网发现");
                                    rows.Add(lv);
                                }
                            }
                            if (list.Count == 0 && rows.Count <= 1)
                                _lblMembersInfo.Text = "在线成员 0（本机已列出；若缺少其他成员，请确认对方也连接了同一服务器、同一网段）";
                            ApplyMemberRows(rows, localIp);
                        });
                    }
                    finally { _queryInProgress = false; }
                });
                queryThread.IsBackground = true;
                queryThread.Start();
            }
            // 上一次网段扫描结果（缓存）也并入目标行
            foreach (string ip in _lastScanIps)
            {
                if (ip == localIp) continue;
                bool exists = false;
                foreach (ListViewItem it in _listMembers.Items)
                {
                    if (it.Text == ip) { exists = true; break; }
                }
                if (!exists)
                {
                    var lv = new ListViewItem(ip);
                    lv.SubItems.Add("在线");
                    lv.SubItems.Add("已通过网段探测发现");
                    rows.Add(lv);
                }
            }
            // 后台并行 Ping 探测整个虚拟网段，弥补 edge peer 表显示不全的问题
            // 降频：每 20 秒一轮（原每 3 秒 254 并发 Ping，是连接后卡顿的主因）
            if (_scanInProgress) return;
            if (Environment.TickCount - _lastScanTick < 20000) return;
            _lastScanTick = Environment.TickCount;
            _scanInProgress = true;
            string scanPrefix = subnetPrefix;
            string scanSelf = localIp;
            var scanThread = new System.Threading.Thread(() =>
            {
                try
                {
                    var extra = Net.ScanOnlineMembers(scanPrefix, 500);
                    UI(() =>
                    {
                        _lastScanIps = extra;
                        foreach (string ip in extra)
                        {
                            if (ip == scanSelf) continue;
                            bool exists = false;
                            foreach (ListViewItem it in _listMembers.Items)
                            {
                                if (it.Text == ip) { exists = true; break; }
                            }
                            if (!exists)
                            {
                                var lv = new ListViewItem(ip);
                                lv.SubItems.Add("在线");
                                lv.SubItems.Add("已通过网段探测发现");
                                _listMembers.Items.Add(lv);
                            }
                        }
                    });
                }
                catch { }
                finally { _scanInProgress = false; }
            });
            scanThread.IsBackground = true;
            scanThread.Start();
        }

        /// <summary>应用成员行增量更新（移除消失的、追加新的）</summary>
        private void ApplyMemberRows(List<ListViewItem> rows, string localIp)
        {
            if (IsDisposed) return;
            _listMembers.BeginUpdate();
            try
            {
                for (int i = _listMembers.Items.Count - 1; i >= 0; i--)
                {
                    bool keep = false;
                    foreach (ListViewItem r in rows)
                    {
                        if (r.Text == _listMembers.Items[i].Text) { keep = true; break; }
                    }
                    if (!keep) _listMembers.Items.RemoveAt(i);
                }
                foreach (ListViewItem r in rows)
                {
                    bool exists = false;
                    foreach (ListViewItem it in _listMembers.Items)
                    {
                        if (it.Text == r.Text) { exists = true; break; }
                    }
                    if (!exists) _listMembers.Items.Add(r);
                }
            }
            finally { _listMembers.EndUpdate(); }
        }

        private void RunTest(string kind)
        {
            // 在 UI 线程读取输入，避免后台线程跨线程访问控件
            string pingHost = _txtPingHost.Text.Trim();
            string tcpHost = _txtTcpHost.Text.Trim();
            string udpHost = _txtUdpHost.Text.Trim();
            int tcpPort; if (!int.TryParse(_txtTcpPort.Text.Trim(), out tcpPort)) tcpPort = 0;
            int udpPort; if (!int.TryParse(_txtUdpPort.Text.Trim(), out udpPort)) udpPort = 0;

            var t = new Thread(() =>
            {
                try
                {
                    string outText = "";
                    switch (kind)
                    {
                        case "ping":
                            if (pingHost.Length == 0) { UI(() => MessageBox.Show("请填写目标 IP", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information)); return; }
                            outText = "Ping " + pingHost + " ...\r\n" + Net.PingTest(pingHost, 4, 2000);
                            break;
                        case "tcp":
                            if (tcpHost.Length == 0 || tcpPort <= 0)
                            {
                                UI(() => MessageBox.Show("请填写目标 IP 和端口", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information)); return;
                            }
                            outText = "TCP 测试 " + tcpHost + ":" + tcpPort + " ...\r\n" + Net.TcpTest(tcpHost, tcpPort, 3000);
                            break;
                        case "udp":
                            if (udpHost.Length == 0 || udpPort <= 0)
                            {
                                UI(() => MessageBox.Show("请填写目标 IP 和端口", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information)); return;
                            }
                            outText = "UDP 探测 " + udpHost + ":" + udpPort + " ...\r\n" + Net.UdpTest(udpHost, udpPort, 3, 2000);
                            break;
                    }
                    string final = outText;
                    UI(() => { _txtTestResult.Text = final; });
                }
                catch (Exception ex)
                {
                    string msg = "出错: " + ex.Message;
                    UI(() => _txtTestResult.AppendText(msg + "\r\n"));
                }
            });
            t.IsBackground = true;
            t.Start();
        }

        private void UI(Action a)
        {
            if (IsDisposed) return;
            if (InvokeRequired) { try { BeginInvoke(a); } catch { } }
            else a();
        }

        private void ShowMoreMenu()
        {
            if (_drawer != null && !_drawer.IsDisposed) return;
            _drawer = new DrawerMenu(this);
            _drawer.Open(
                new[] { "联机配置", "安装 TAP 驱动", "卸载 TAP 驱动", "配置防火墙", "查看运行日志", "查看使用指南" },
                new Action[] {
                    () => OpenConnectDialog(),
                    () => MessageBox.Show(Net.InstallTap(), "提示", MessageBoxButtons.OK, MessageBoxIcon.Information),
                    () => MessageBox.Show(Net.UninstallTap(), "提示", MessageBoxButtons.OK, MessageBoxIcon.Information),
                    () => MessageBox.Show(Net.SetFirewall(_cfg.SupernodePort, _cfg.LocalPort), "提示", MessageBoxButtons.OK, MessageBoxIcon.Information),
                    () => ShowLog(),
                    () => ShowPage("guide")
                });
        }

        private void ShowLog()
        {
            using (var f = new ChromeForm())
            {
                f.Text = "edge 运行日志";
                f.StartPosition = FormStartPosition.CenterParent;
                f.Size = new Size(680, 460);
                f.MinimumSize = new Size(520, 320);
                var tb = new LogBox();
                tb.Dock = DockStyle.Fill;
                tb.Font = new Font("Consolas", 9f);
                tb.Text = Net.GetEdgeLog();
                f.Controls.Add(tb);
                f.SetupChrome("运行日志", false);
                f.ShowDialog(this);
            }
        }

        private void OpenConnectDialog()
        {
            using (var dlg = new ConnectDialog(_cfg))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    dlg.ApplyTo(_cfg);
                    _cfg.Save();
                    BuildOfficialList();
                    _txtSegment.Text = _cfg.Segment;
                    _txtSuffix.Text = _cfg.Suffix;
                    _txtCustom.Text = _cfg.CustomServer;
                    if (_txtPrefix != null) _txtPrefix.Text = _cfg.NetworkPrefix;
                    UpdateAutoIpState();
                }
            }
        }

        // ==========================================================
        // 自检截图（--shot 参数触发，用于开发者视觉自测）
        // ==========================================================
        public void DebugShot(string dir)
        {
            try
            {
                System.IO.Directory.CreateDirectory(dir);
                bool origDark = _t.IsDark;
                bool origDarkCfg = _cfg.Dark;

                // 暗色
                if (!_t.IsDark) { ToggleTheme(); Application.DoEvents(); }
                ShowPage("main"); Application.DoEvents();
                SaveShot(System.IO.Path.Combine(dir, "main_dark.png"));
                // 自定义服务器页（验证输入框占位符不再被裁剪）
                try
                {
                    _serverSeg.SelectIndex(1); Application.DoEvents();
                    SaveShot(System.IO.Path.Combine(dir, "main_custom_dark.png"));
                    // 临时填入值，验证 UserPaint 下输入文本仍正常渲染
                    string saved = _txtCustom.Text;
                    _txtCustom.Text = "abcd.at.ply.gg:1234";
                    Application.DoEvents();
                    SaveShot(System.IO.Path.Combine(dir, "main_custom_filled_dark.png"));
                    _txtCustom.Text = saved;
                    _serverSeg.SelectIndex(0); Application.DoEvents();
                }
                catch { }
                ShowPage("members"); Application.DoEvents(); SaveShot(System.IO.Path.Combine(dir, "members_dark.png"));
                ShowPage("test"); Application.DoEvents(); SaveShot(System.IO.Path.Combine(dir, "test_dark.png"));
                ShowPage("guide"); Application.DoEvents(); SaveShot(System.IO.Path.Combine(dir, "guide_dark.png"));
                ShowPage("main");

                // 引导气泡视觉（暗色，第 1 步）
                try
                {
                    var tour = new FocusTour(this, new FocusTour.TourStep[] {
                        new FocusTour.TourStep { Target = _serverSeg, Title = "第一步 · 选择服务器",
                            Text = "官方服务器 = 免费公共节点，最快开始；自定义 = 填好友或自己搭的中继地址。" },
                        new FocusTour.TourStep { Target = _txtSegment, Title = "第二步 · 配置虚拟 IP",
                            Text = "网段大家填一样，后缀每人不同。" },
                        new FocusTour.TourStep { Target = _btnConnect, Title = "第三步 · 点「连接」",
                            Text = "连上后顶部状态变绿。" },
                        new FocusTour.TourStep { Target = _navMore, Title = "第四步 · 更多功能",
                            Text = "安装 TAP、配置防火墙都在这里。" },
                    });
                    tour.Start();
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(200);
                    if (tour.Bubble != null)
                        SaveControl(tour.Bubble, System.IO.Path.Combine(dir, "tour_bubble_dark.png"));
                    tour.RequestClose();
                    Application.DoEvents();
                }
                catch (Exception tex)
                {
                    try { System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "tour_error.txt"), tex.ToString()); } catch { }
                }

                // 抽屉（暗色）
                ShowMoreMenu();
                for (int i = 0; i < 30; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(12); }
                if (_drawer != null && !_drawer.IsDisposed)
                    SaveControl(_drawer, System.IO.Path.Combine(dir, "drawer_panel_dark.png"));
                if (_drawer != null && !_drawer.IsDisposed) _drawer.Close();
                for (int i = 0; i < 20; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(12); }

                // 联机配置弹窗（暗色，非模态）
                using (var dlg = new ConnectDialog(_cfg))
                {
                    dlg.StartPosition = FormStartPosition.Manual;
                    dlg.Location = new Point(Location.X + 140, Location.Y + 90);
                    dlg.Show(this);
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(200);
                    SaveControl(dlg, System.IO.Path.Combine(dir, "dialog_dark.png"));
                    dlg.Close();
                    Application.DoEvents();
                }

                // 亮色
                ToggleTheme(); Application.DoEvents();
                ShowPage("main"); Application.DoEvents();
                SaveShot(System.IO.Path.Combine(dir, "main_light.png"));
                ShowPage("members"); Application.DoEvents(); SaveShot(System.IO.Path.Combine(dir, "members_light.png"));
                ShowPage("test"); Application.DoEvents(); SaveShot(System.IO.Path.Combine(dir, "test_light.png"));
                ShowPage("guide"); Application.DoEvents(); SaveShot(System.IO.Path.Combine(dir, "guide_light.png"));

                // 抽屉（亮色）
                ShowMoreMenu();
                for (int i = 0; i < 30; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(12); }
                if (_drawer != null && !_drawer.IsDisposed)
                    SaveControl(_drawer, System.IO.Path.Combine(dir, "drawer_panel_light.png"));
                if (_drawer != null && !_drawer.IsDisposed) _drawer.Close();
                for (int i = 0; i < 20; i++) { Application.DoEvents(); System.Threading.Thread.Sleep(12); }

                // 引导气泡（亮色）
                try
                {
                    var tour2 = new FocusTour(this, new FocusTour.TourStep[] {
                        new FocusTour.TourStep { Target = _serverSeg, Title = "第一步 · 选择服务器",
                            Text = "官方服务器 = 免费公共节点，最快开始；自定义 = 填好友或自己搭的中继地址。" },
                        new FocusTour.TourStep { Target = _txtSegment, Title = "第二步 · 配置虚拟 IP",
                            Text = "网段大家填一样，后缀每人不同。" },
                        new FocusTour.TourStep { Target = _btnConnect, Title = "第三步 · 点「连接」",
                            Text = "连上后顶部状态变绿。" },
                        new FocusTour.TourStep { Target = _navMore, Title = "第四步 · 更多功能",
                            Text = "安装 TAP、配置防火墙都在这里。" },
                    });
                    tour2.Start();
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(200);
                    if (tour2.Bubble != null)
                        SaveControl(tour2.Bubble, System.IO.Path.Combine(dir, "tour_bubble_light.png"));
                    tour2.RequestClose();
                    Application.DoEvents();
                }
                catch { }

                // 恢复原主题
                if (_t.IsDark != origDark) ToggleTheme();
                _cfg.Dark = origDarkCfg;
                _cfg.Save();
            }
            catch (Exception ex)
            {
                try { System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "error.txt"), ex.ToString()); } catch { }
            }
        }

        private void SaveShot(string path)
        {
            using (var bmp = new Bitmap(ClientSize.Width, ClientSize.Height))
            {
                DrawToBitmap(bmp, new Rectangle(0, 0, ClientSize.Width, ClientSize.Height));
                bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
        }

        private void SaveControl(Control c, string path)
        {
            using (var bmp = new Bitmap(c.Width, c.Height))
            {
                c.DrawToBitmap(bmp, new Rectangle(0, 0, c.Width, c.Height));
                bmp.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
        }
    }
}








