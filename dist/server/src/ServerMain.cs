using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace NtoNServerControl
{
    /// <summary>
    /// NtoNServer 控制台主窗体：侧栏三页（概览/设置/日志）+ 托盘守护 + 双主题。
    /// 关闭窗口即最小化到托盘，后台继续守护 supernode，全程无命令行窗口。
    /// </summary>
    public class MainForm : ChromeForm
    {
        private ServerConfig _cfg;
        private bool _reallyQuit = false;
        private bool _autoGuard;

        // 侧栏
        private NavItem _navHome, _navSettings, _navLog;
        private FlatButton _btnTheme;
        private Label _lblVersion;

        // 概览页
        private Panel _pageHome;
        private StatusDot _dotStatus;
        private Label _lblMeta, _lblGuard, _lblGuardDetail;
        private GButton _btnStart;
        private FlatButton _btnRestart, _btnStop;

        // 设置页
        private Panel _pageSettings;
        private RoundedTextBox _txtPort, _txtMgmt;
        private ThemedCheckBox _chkGuard, _chkAutoStart;
        private GButton _btnSave;
        private FlatButton _btnFirewall;
        private Label _lblSaved;

        // 日志页
        private Panel _pageLog;
        private LogBox _logBox;
        private FlatButton _btnRefreshLog;

        private Timer _tmState, _tmGuard;
        private NotifyIcon _tray;
        private ContextMenuStrip _trayMenu;
        private IntPtr _iconHandle;
        private System.Drawing.Icon _trayIcon;

        public MainForm()
        {
            _cfg = ServerConfig.Load();
            _autoGuard = _cfg.AutoGuard;
            Theme.Current = _cfg.Dark ? Theme.Dark : Theme.Light;

            Text = "NtoNServer 控制台";
            BackColor = Theme.Current.WindowBg;
            ClientSize = new Size(1000, 660);
            MinimumSize = new Size(900, 600);

            BuildPages();
            BuildSidebar();
            BuildTray();
            SetupChrome("NtoNServer 控制台", true);
            Theme.Apply(this);
            ShowPage(0);

            _tmState = new Timer();
            _tmState.Interval = 1000;
            _tmState.Tick += (s, e) => RefreshState();
            _tmState.Start();

            _tmGuard = new Timer();
            _tmGuard.Interval = 5000;
            _tmGuard.Tick += (s, e) => GuardTick();
            _tmGuard.Start();
        }

        // ================= 侧栏 =================
        private void BuildSidebar()
        {
            var side = new Panel();
            side.Dock = DockStyle.Left;
            side.Width = 200;
            side.BackColor = Theme.Current.SidebarBg;
            Controls.Add(side);

            var logo = new GradientLabel();
            logo.Text = "NtoNServer";
            logo.Font = new Font("Microsoft YaHei UI", 13f, FontStyle.Bold);
            logo.Location = new Point(16, 16);
            logo.Size = new Size(170, 30);
            side.Controls.Add(logo);

            _navHome = MakeNav("概览", 62);
            _navSettings = MakeNav("设置", 108);
            _navLog = MakeNav("日志", 154);
            side.Controls.Add(_navHome);
            side.Controls.Add(_navSettings);
            side.Controls.Add(_navLog);
            _navHome.OnClickNav = (s, e) => ShowPage(0);
            _navSettings.OnClickNav = (s, e) => ShowPage(1);
            _navLog.OnClickNav = (s, e) => ShowPage(2);

            // 侧栏底部：主题切换 + 版本
            var footer = new Panel();
            footer.Dock = DockStyle.Bottom;
            footer.Height = 100;
            footer.BackColor = Theme.Current.SidebarBg;
            side.Controls.Add(footer);

            _btnTheme = new FlatButton();
            _btnTheme.Kind = FlatButton.BtnKind.Ghost;
            _btnTheme.Fill = Theme.Current.SidebarBg;
            _btnTheme.Location = new Point(16, 12);
            _btnTheme.Size = new Size(168, 36);
            _btnTheme.Text = Theme.Current.IsDark ? "☾ 亮色模式" : "○ 暗色模式";
            _btnTheme.Click += (s, e) => ToggleTheme();
            footer.Controls.Add(_btnTheme);

            _lblVersion = new Label();
            _lblVersion.Text = "v1.0 · n2n supernode";
            _lblVersion.Font = new Font("Microsoft YaHei UI", 8.5f);
            _lblVersion.ForeColor = Theme.Current.TextFaint;
            _lblVersion.AutoSize = false;
            _lblVersion.Location = new Point(20, 60);
            _lblVersion.Size = new Size(160, 20);
            footer.Controls.Add(_lblVersion);

            // 侧栏右分隔线
            var sep = new Panel();
            sep.Dock = DockStyle.Right;
            sep.Width = 1;
            sep.BackColor = Theme.Current.Border;
            side.Controls.Add(sep);
        }

        private NavItem MakeNav(string text, int y)
        {
            var n = new NavItem();
            n.Text = text;
            n.Location = new Point(8, y);
            n.Size = new Size(184, 46);
            return n;
        }

        // ================= 页面 =================
        private void BuildPages()
        {
            var content = new Panel();
            content.Dock = DockStyle.Fill;
            content.BackColor = Theme.Current.WindowBg;
            Controls.Add(content);

            BuildHomePage(content);
            BuildSettingsPage(content);
            BuildLogPage(content);
        }

        private void BuildHomePage(Panel content)
        {
            _pageHome = new Panel();
            _pageHome.Dock = DockStyle.Fill;
            _pageHome.BackColor = Theme.Current.WindowBg;
            content.Controls.Add(_pageHome);

            var head = new GradientLabel();
            head.Text = "概览";
            head.Font = new Font("Microsoft YaHei UI", 15f, FontStyle.Bold);
            head.Location = new Point(28, 22);
            head.Size = new Size(200, 30);
            _pageHome.Controls.Add(head);

            var sub = new Label();
            sub.Text = "服务端运行状态与控制";
            sub.Font = new Font("Microsoft YaHei UI", 9.5f);
            sub.ForeColor = Theme.Current.TextDim;
            sub.AutoSize = false;
            sub.Location = new Point(28, 56);
            sub.Size = new Size(500, 22);
            _pageHome.Controls.Add(sub);

            // —— 服务状态卡 ——
            var card = new RoundedPanel();
            card.ShowBorder = true;
            card.Location = new Point(28, 92);
            card.Size = new Size(760, 210);
            card.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            _pageHome.Controls.Add(card);

            var t1 = new GradientLabel();
            t1.Text = "服务状态";
            t1.Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold);
            t1.Location = new Point(22, 14);
            t1.Size = new Size(140, 24);
            card.Controls.Add(t1);

            _dotStatus = new StatusDot();
            _dotStatus.Location = new Point(22, 52);
            _dotStatus.Size = new Size(420, 30);
            card.Controls.Add(_dotStatus);

            _lblMeta = new Label();
            _lblMeta.Font = new Font("Microsoft YaHei UI", 9.5f);
            _lblMeta.ForeColor = Theme.Current.TextDim;
            _lblMeta.AutoSize = false;
            _lblMeta.Location = new Point(22, 90);
            _lblMeta.Size = new Size(460, 24);
            card.Controls.Add(_lblMeta);

            _lblGuard = new Label();
            _lblGuard.Font = new Font("Microsoft YaHei UI", 9.5f);
            _lblGuard.ForeColor = Theme.Current.TextDim;
            _lblGuard.AutoSize = false;
            _lblGuard.Location = new Point(22, 118);
            _lblGuard.Size = new Size(460, 24);
            card.Controls.Add(_lblGuard);

            _lblGuardDetail = new Label();
            _lblGuardDetail.Font = new Font("Microsoft YaHei UI", 8.5f);
            _lblGuardDetail.ForeColor = Theme.Current.TextFaint;
            _lblGuardDetail.AutoSize = false;
            _lblGuardDetail.Location = new Point(22, 148);
            _lblGuardDetail.Size = new Size(460, 48);
            card.Controls.Add(_lblGuardDetail);

            // 右侧按钮区
            _btnStart = new GButton();
            _btnStart.Text = "启动服务";
            _btnStart.Location = new Point(540, 26);
            _btnStart.Size = new Size(190, 46);
            _btnStart.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnStart.Click += (s, e) => StartService();
            card.Controls.Add(_btnStart);

            _btnRestart = new FlatButton();
            _btnRestart.Kind = FlatButton.BtnKind.Soft;
            _btnRestart.Text = "重启服务";
            _btnRestart.Location = new Point(540, 84);
            _btnRestart.Size = new Size(190, 42);
            _btnRestart.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnRestart.Click += (s, e) => RestartService();
            card.Controls.Add(_btnRestart);

            _btnStop = new FlatButton();
            _btnStop.Kind = FlatButton.BtnKind.Danger;
            _btnStop.Text = "停止服务";
            _btnStop.Location = new Point(540, 136);
            _btnStop.Size = new Size(190, 42);
            _btnStop.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnStop.Click += (s, e) => StopService();
            card.Controls.Add(_btnStop);

            // —— 后台守护卡 ——
            var guard = new RoundedPanel();
            guard.ShowBorder = true;
            guard.Location = new Point(28, 318);
            guard.Size = new Size(760, 150);
            guard.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            _pageHome.Controls.Add(guard);

            var g1 = new GradientLabel();
            g1.Text = "后台守护";
            g1.Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold);
            g1.Location = new Point(22, 14);
            g1.Size = new Size(140, 24);
            guard.Controls.Add(g1);

            var gd = new Label();
            gd.Text = "服务崩溃或意外退出时会自动重新拉起；关闭本窗口不停止守护，程序将最小化到托盘持续运行。\n托盘图标：双击恢复窗口，右键可退出守护。";
            gd.Font = new Font("Microsoft YaHei UI", 9.5f);
            gd.ForeColor = Theme.Current.TextDim;
            gd.AutoSize = false;
            gd.Location = new Point(22, 50);
            gd.Size = new Size(760, 44);
            guard.Controls.Add(gd);

            var gstate = new Label();
            gstate.Name = "lblGuardState";
            gstate.Font = new Font("Microsoft YaHei UI", 9.5f);
            gstate.ForeColor = Theme.Current.Success;
            gstate.AutoSize = false;
            gstate.Location = new Point(22, 104);
            gstate.Size = new Size(400, 24);
            guard.Controls.Add(gstate);
        }

        private void BuildSettingsPage(Panel content)
        {
            _pageSettings = new Panel();
            _pageSettings.Dock = DockStyle.Fill;
            _pageSettings.BackColor = Theme.Current.WindowBg;
            content.Controls.Add(_pageSettings);

            var head = new GradientLabel();
            head.Text = "设置";
            head.Font = new Font("Microsoft YaHei UI", 15f, FontStyle.Bold);
            head.Location = new Point(28, 22);
            head.Size = new Size(200, 30);
            _pageSettings.Controls.Add(head);

            var sub = new Label();
            sub.Text = "连接参数与系统选项（修改后点击“保存设置”生效）";
            sub.Font = new Font("Microsoft YaHei UI", 9.5f);
            sub.ForeColor = Theme.Current.TextDim;
            sub.AutoSize = false;
            sub.Location = new Point(28, 56);
            sub.Size = new Size(600, 22);
            _pageSettings.Controls.Add(sub);

            // —— 连接参数卡 ——
            var card = new RoundedPanel();
            card.ShowBorder = true;
            card.Location = new Point(28, 92);
            card.Size = new Size(760, 230);
            card.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            _pageSettings.Controls.Add(card);

            var t1 = new GradientLabel();
            t1.Text = "连接参数";
            t1.Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold);
            t1.Location = new Point(22, 14);
            t1.Size = new Size(140, 24);
            card.Controls.Add(t1);

            var lPort = new Label();
            lPort.Text = "UDP 端口";
            lPort.Font = new Font("Microsoft YaHei UI", 9.5f);
            lPort.ForeColor = Theme.Current.TextDim;
            lPort.Location = new Point(22, 60);
            lPort.Size = new Size(110, 30);
            card.Controls.Add(lPort);

            _txtPort = new RoundedTextBox();
            _txtPort.Placeholder = "3301";
            _txtPort.Text = _cfg.Port.ToString();
            _txtPort.Location = new Point(140, 56);
            _txtPort.Size = new Size(200, 36);
            card.Controls.Add(_txtPort);

            var lMgmt = new Label();
            lMgmt.Text = "管理端口";
            lMgmt.Font = new Font("Microsoft YaHei UI", 9.5f);
            lMgmt.ForeColor = Theme.Current.TextDim;
            lMgmt.Location = new Point(22, 108);
            lMgmt.Size = new Size(110, 30);
            card.Controls.Add(lMgmt);

            _txtMgmt = new RoundedTextBox();
            _txtMgmt.Placeholder = "0 = 不启用";
            _txtMgmt.Text = _cfg.MgmtPort > 0 ? _cfg.MgmtPort.ToString() : "";
            _txtMgmt.Location = new Point(140, 104);
            _txtMgmt.Size = new Size(200, 36);
            card.Controls.Add(_txtMgmt);

            _chkGuard = new ThemedCheckBox();
            _chkGuard.Text = "自动守护（服务崩溃自动重启）";
            _chkGuard.Checked = _cfg.AutoGuard;
            _chkGuard.Location = new Point(24, 160);
            _chkGuard.Size = new Size(280, 32);
            card.Controls.Add(_chkGuard);

            var hint = new Label();
            hint.Text = "端口需在路由器/防火墙放行，公网用户才能连接；修改端口后请重新“放行防火墙”。";
            hint.Font = new Font("Microsoft YaHei UI", 8.5f);
            hint.ForeColor = Theme.Current.TextFaint;
            hint.AutoSize = false;
            hint.Location = new Point(380, 60);
            hint.Size = new Size(520, 100);
            card.Controls.Add(hint);

            // —— 系统卡 ——
            var sys = new RoundedPanel();
            sys.ShowBorder = true;
            sys.Location = new Point(28, 338);
            sys.Size = new Size(760, 140);
            sys.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            _pageSettings.Controls.Add(sys);

            var s1 = new GradientLabel();
            s1.Text = "系统";
            s1.Font = new Font("Microsoft YaHei UI", 11f, FontStyle.Bold);
            s1.Location = new Point(22, 14);
            s1.Size = new Size(140, 24);
            sys.Controls.Add(s1);

            _chkAutoStart = new ThemedCheckBox();
            _chkAutoStart.Text = "开机自启（登录后自动后台运行并守护）";
            _chkAutoStart.Checked = _cfg.AutoStart;
            _chkAutoStart.Location = new Point(24, 56);
            _chkAutoStart.Size = new Size(320, 32);
            _chkAutoStart.CheckedChanged += (s, e) =>
            {
                ServerCore.SetAutoStart(_chkAutoStart.Checked);
                _cfg.AutoStart = _chkAutoStart.Checked;
                _cfg.Save();
            };
            sys.Controls.Add(_chkAutoStart);

            _btnFirewall = new FlatButton();
            _btnFirewall.Kind = FlatButton.BtnKind.Soft;
            _btnFirewall.Text = "放行防火墙";
            _btnFirewall.Location = new Point(24, 96);
            _btnFirewall.Size = new Size(150, 36);
            _btnFirewall.Click += (s, e) => ApplyFirewall();
            sys.Controls.Add(_btnFirewall);

            _btnSave = new GButton();
            _btnSave.Text = "保存设置";
            _btnSave.Location = new Point(28, 500);
            _btnSave.Size = new Size(160, 46);
            _btnSave.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            _btnSave.Click += (s, e) => SaveSettings();
            _pageSettings.Controls.Add(_btnSave);

            _lblSaved = new Label();
            _lblSaved.Font = new Font("Microsoft YaHei UI", 9.5f);
            _lblSaved.ForeColor = Theme.Current.Success;
            _lblSaved.AutoSize = false;
            _lblSaved.Location = new Point(204, 508);
            _lblSaved.Size = new Size(360, 26);
            _pageSettings.Controls.Add(_lblSaved);
        }

        private void BuildLogPage(Panel content)
        {
            _pageLog = new Panel();
            _pageLog.Dock = DockStyle.Fill;
            _pageLog.BackColor = Theme.Current.WindowBg;
            content.Controls.Add(_pageLog);

            var head = new GradientLabel();
            head.Text = "日志";
            head.Font = new Font("Microsoft YaHei UI", 15f, FontStyle.Bold);
            head.Location = new Point(28, 22);
            head.Size = new Size(200, 30);
            _pageLog.Controls.Add(head);

            var sub = new Label();
            sub.Text = "supernode 运行日志（自动刷新）";
            sub.Font = new Font("Microsoft YaHei UI", 9.5f);
            sub.ForeColor = Theme.Current.TextDim;
            sub.AutoSize = false;
            sub.Location = new Point(28, 56);
            sub.Size = new Size(500, 22);
            _pageLog.Controls.Add(sub);

            _btnRefreshLog = new FlatButton();
            _btnRefreshLog.Kind = FlatButton.BtnKind.Soft;
            _btnRefreshLog.Text = "刷新日志";
            _btnRefreshLog.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            _btnRefreshLog.Location = new Point(820, 48);
            _btnRefreshLog.Size = new Size(120, 36);
            _btnRefreshLog.Click += (s, e) => RefreshLog();
            _pageLog.Controls.Add(_btnRefreshLog);

            _logBox = new LogBox();
            _logBox.Location = new Point(28, 92);
            _logBox.Size = new Size(760, 470);
            _logBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            _logBox.BackColor = Theme.Current.CardBg;
            _logBox.ForeColor = Theme.Current.Text;
            _pageLog.Controls.Add(_logBox);

            RefreshLog();
        }

        // ================= 托盘 =================
        private void BuildTray()
        {
            _tray = new NotifyIcon();
            _tray.Text = "NtoNServer 控制台";
            _tray.Icon = BuildIcon();
            _tray.Visible = true;
            _tray.DoubleClick += (s, e) => ShowFromTray();

            _trayMenu = new ContextMenuStrip();
            _trayMenu.BackColor = Theme.Current.CardBg;
            _trayMenu.ForeColor = Theme.Current.Text;
            _trayMenu.Items.Add("打开控制台", null, (s, e) => ShowFromTray());
            _trayMenu.Items.Add("启动服务", null, (s, e) => StartService());
            _trayMenu.Items.Add("停止服务", null, (s, e) => StopService());
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add("退出守护", null, (s, e) => QuitApp());
            _tray.ContextMenuStrip = _trayMenu;
        }

        private System.Drawing.Icon BuildIcon()
        {
            var bmp = new Bitmap(32, 32);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var b = Theme.Accent(new Rectangle(2, 2, 28, 28)))
                using (var path = RoundedPanel.RoundedRect(new Rectangle(2, 2, 28, 28), 8))
                    g.FillPath(b, path);
                TextRenderer.DrawText(g, "N", new Font("Segoe UI", 12, FontStyle.Bold), new Rectangle(2, 2, 28, 28),
                    Color.White, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
            _iconHandle = bmp.GetHicon();
            _trayIcon = System.Drawing.Icon.FromHandle(_iconHandle);
            return _trayIcon;
        }

        // ================= 业务动作 =================
        private void StartService()
        {
            int port = GetPort();
            int mgmt = GetMgmt();
            if (ServerCore.Start(port, mgmt))
            {
                _cfg.Port = port; _cfg.MgmtPort = mgmt; _cfg.Save();
                ShowTrayTip("服务已启动", "supernode 正在后台运行（UDP " + port + "）");
            }
            else
            {
                MessageBox.Show(this, "启动失败：无法启动 supernode.exe。", "NtoNServer", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            RefreshState();
        }

        private void StopService()
        {
            ServerCore.Stop();
            ShowTrayTip("服务已停止", "supernode 已停止");
            RefreshState();
        }

        private void RestartService()
        {
            ServerCore.Stop();
            System.Threading.Thread.Sleep(600);
            StartService();
        }

        private void ApplyFirewall()
        {
            int port = GetPort();
            bool ok = ServerCore.ApplyFirewall(port);
            _lblSaved.Text = ok ? "防火墙已放行 UDP/TCP " + port : "防火墙配置失败（需以管理员运行）";
            _lblSaved.ForeColor = Theme.Current.Success;
        }

        private void SaveSettings()
        {
            int port = GetPort();
            int mgmt = GetMgmt();
            _cfg.Port = port;
            _cfg.MgmtPort = mgmt;
            _cfg.AutoGuard = _chkGuard.Checked;
            _autoGuard = _chkGuard.Checked;
            _cfg.Save();
            _lblSaved.Text = "已保存 · 端口 " + port + " · 守护 " + (_autoGuard ? "开" : "关");
            _lblSaved.ForeColor = Theme.Current.Success;
            RefreshState();
        }

        private int GetPort()
        {
            int v;
            if (_txtPort != null && int.TryParse(_txtPort.Text, out v) && v >= 1 && v <= 65535) return v;
            return _cfg.Port;
        }

        private int GetMgmt()
        {
            int v;
            if (_txtMgmt != null && int.TryParse(_txtMgmt.Text, out v) && v >= 1 && v <= 65535) return v;
            return 0;
        }

        private void GuardTick()
        {
            if (!_autoGuard) return;
            if (!ServerCore.IsRunning())
            {
                ServerCore.Start(_cfg.Port, _cfg.MgmtPort);
            }
        }

        // ================= 状态刷新 =================
        public void RefreshState()
        {
            try
            {
                bool run = ServerCore.IsRunning();
                Process p = ServerCore.GetRunning();
                var t = Theme.Current;
                if (_dotStatus != null)
                {
                    if (run)
                    {
                        _dotStatus.Set("● 运行中", t.Success);
                        _lblMeta.Text = p != null
                            ? "PID " + p.Id + "  ·  内存 " + Math.Round(p.WorkingSet64 / 1048576.0, 1) + " MB  ·  UDP " + _cfg.Port
                            : "UDP 端口 " + _cfg.Port;
                    }
                    else
                    {
                        _dotStatus.Set("● 已停止", t.TextFaint);
                        _lblMeta.Text = "尚未运行 · 点击“启动服务”或等待守护自动拉起";
                    }
                    _lblGuard.Text = _autoGuard
                        ? "后台守护：已开启（每 5 秒检测，崩溃自动重启）"
                        : "后台守护：已关闭（可在“设置”中开启）";
                    _lblGuardDetail.Text = run
                        ? "日志文件：" + ServerCore.LogPath
                        : "本程序关闭窗口后仍驻留托盘，守护持续生效。";
                }
                if (_chkGuard != null) { _chkGuard.Checked = _autoGuard; }
            }
            catch { }
        }

        private void RefreshLog()
        {
            try { if (_logBox != null) _logBox.Text = ServerCore.ReadLog(300); }
            catch { }
        }

        // ================= 主题 / 页面 / 托盘交互 =================
        private void ToggleTheme()
        {
            bool dark = Theme.Current.IsDark;
            Theme.Switch(this, !dark);
            _btnTheme.Text = Theme.Current.IsDark ? "☾ 亮色模式" : "○ 暗色模式";
            _btnTheme.Fill = Theme.Current.SidebarBg;
            _cfg.Dark = Theme.Current.IsDark;
            _cfg.Save();
            Theme.Apply(this);
            ApplyCustomThemeColors();
            RefreshState();
        }

        private void ApplyCustomThemeColors()
        {
            var t = Theme.Current;
            _dotStatus.TextFont = new Font("Microsoft YaHei UI", 10f);
            _dotStatus.Invalidate();
            _lblMeta.ForeColor = t.TextDim;
            _lblGuard.ForeColor = t.TextDim;
            _lblGuardDetail.ForeColor = t.TextFaint;
            _lblVersion.ForeColor = t.TextFaint;
            _logBox.BackColor = t.CardBg;
            _logBox.ForeColor = t.Text;
            _lblSaved.ForeColor = t.Success;
            _trayMenu.BackColor = t.CardBg;
            _trayMenu.ForeColor = t.Text;
            Invalidate();
        }

        private void ShowPage(int idx)
        {
            _navHome.Active = idx == 0;
            _navSettings.Active = idx == 1;
            _navLog.Active = idx == 2;
            _pageHome.Visible = idx == 0;
            _pageSettings.Visible = idx == 1;
            _pageLog.Visible = idx == 2;
            if (idx == 2) RefreshLog();
        }

        private void HideToTray()
        {
            Hide();
            ShowTrayTip("NtoNServer 守护运行中", "双击托盘图标恢复窗口，右键可退出守护。");
        }

        private void ShowFromTray()
        {
            Show();
            WindowState = FormWindowState.Normal;
            BringToFront();
            Activate();
        }

        private void QuitApp()
        {
            _reallyQuit = true;
            _tmState.Stop();
            _tmGuard.Stop();
            if (ServerCore.IsRunning())
            {
                var r = MessageBox.Show(this, "服务正在运行，退出前是否停止 supernode？\n（选“否”则服务继续在后台运行）",
                    "退出守护", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (r == DialogResult.Cancel) { _reallyQuit = false; _tmState.Start(); _tmGuard.Start(); ShowFromTray(); return; }
                if (r == DialogResult.Yes) ServerCore.Stop();
            }
            _tray.Visible = false;
            Close();
        }

        private void ShowTrayTip(string title, string text)
        {
            try { _tray.ShowBalloonTip(2500, title, text, ToolTipIcon.Info); } catch { }
        }

        // —— 自检 / 静默启动调试入口 ——
        public void ToggleThemePublic() { ToggleTheme(); }
        public void DebugShowPage(int idx) { ShowPage(idx); }

        // --capture 专用：无副作用强制主题（不写 config）
        public void ApplyThemeCapture(bool dark)
        {
            Theme.Switch(this, dark);
            _btnTheme.Text = dark ? "☾ 亮色模式" : "○ 暗色模式";
            _btnTheme.Fill = Theme.Current.SidebarBg;
            ApplyCustomThemeColors();
            RefreshState();
        }
        public void DebugRefreshLog() { RefreshLog(); }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_reallyQuit)
            {
                CleanupIcon();
                base.OnFormClosing(e);
                return;
            }
            // 关闭窗口 = 最小化到托盘继续守护
            e.Cancel = true;
            HideToTray();
        }

        private void CleanupIcon()
        {
            try { if (_trayIcon != null) _trayIcon.Dispose(); } catch { }
            try { if (_iconHandle != IntPtr.Zero) { NativeMethods.DestroyIcon(_iconHandle); _iconHandle = IntPtr.Zero; } } catch { }
        }

        private static class NativeMethods
        {
            [System.Runtime.InteropServices.DllImport("user32.dll")]
            public static extern bool DestroyIcon(IntPtr handle);
        }
    }
}
