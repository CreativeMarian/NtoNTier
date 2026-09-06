using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace NtoNTier
{
    /// <summary>
    /// 内置文件浏览器页（WebView2 = Chromium 内核，微软免费，随系统 WebView2 Runtime 运行）。
    /// 用户在页面里点下载时，通过 CoreWebView2.DownloadStarting 拦截默认下载，
    /// 交给 DownloadManager 分片多线程引擎（IDM 式）。
    /// 纯 C#5 / .NET 4.8 兼容。
    /// </summary>
    public class BrowserPage : Panel
    {
        private WebView2 _browser;
        private RoundedTextBox _addr;
        private FlatButton _btnBack, _btnForward, _btnRefresh, _btnCancel, _btnGo;
        private ListView _taskList;
        private ContextMenuStrip _taskMenu;
        private ToolStripMenuItem _miPauseResume, _miRestart, _miDelete, _miOpenFile, _miOpenFolder;
        private bool _inited;

        /// <summary>下载完成通知（供 MainForm 弹提示等）</summary>
        public event Action<DownloadTask> DownloadFinished;

        public BrowserPage()
        {
            BuildUi();
        }

        public bool IsReady
        {
            get { return _browser != null && _browser.CoreWebView2 != null; }
        }

        /// <summary>初始化 WebView2 并挂下载拦截</summary>
        public void Initialize()
        {
            if (_inited) return;
            _inited = true;
            try
            {
                // 禁用系统代理：避免 Clash/V2Ray 等代理导致虚拟内网地址(10.x)访问空白
                Environment.SetEnvironmentVariable("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS", "--no-proxy-server");
                // 显式设置用户数据文件夹，避免默认位置损坏或权限问题
                string udf = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NtoNTier", "WebView2");
                try { Directory.CreateDirectory(udf); } catch { }
                var env = Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateAsync(null, udf, null).Result;
                _browser = new WebView2();
                _browser.Dock = DockStyle.Fill;
                Controls.Add(_browser);
                _browser.BringToFront();
                _browser.CoreWebView2InitializationCompleted += (s, e) =>
                {
                    if (e.IsSuccess && _browser.CoreWebView2 != null)
                    {
                        _browser.CoreWebView2.DownloadStarting += OnDownloadStarting;
                        _browser.CoreWebView2.NavigationStarting += OnNavStarting;
                        _browser.CoreWebView2.NewWindowRequested += OnNewWindow;
                        _browser.CoreWebView2.NavigationCompleted += OnNavCompleted;
                    }
                };
                _browser.EnsureCoreWebView2Async(env);
            }
            catch (Exception ex)
            {
                // 初始化失败时显示错误，而不是空白
                var lbl = new Label();
                lbl.Text = "WebView2 初始化失败：" + ex.Message + "\n\n请确保已安装 WebView2 Runtime（安装包已自带）。";
                lbl.Dock = DockStyle.Fill;
                lbl.ForeColor = Color.FromArgb(234, 102, 104);
                lbl.Font = new Font("Microsoft YaHei UI", 10f);
                lbl.TextAlign = ContentAlignment.MiddleCenter;
                Controls.Add(lbl);
                lbl.BringToFront();
            }
        }

        /// <summary>导航完成检测：失败时显示错误信息而不是空白页</summary>
        private void OnNavCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            try
            {
                if (!e.IsSuccess)
                {
                    string err = "页面加载失败（错误码：" + e.WebErrorStatus + "）\n\n可能原因：\n1. HFS 未启动或端口不对\n2. 虚拟 IP 未连通\n3. 地址输入有误\n\n请检查后点击刷新重试。";
                    string html = "<html><body style='background:#1e1e2e;color:#e0e0e0;font-family:Microsoft YaHei;padding:40px;'><div style='max-width:500px;margin:80px auto;'><h2 style='color:#ea6668;'>无法访问此页面</h2><p style='line-height:1.8;white-space:pre-line;'>" + err + "</p></div></body></html>";
                    if (_browser != null && _browser.CoreWebView2 != null)
                        _browser.CoreWebView2.NavigateToString(html);
                }
            }
            catch { }
        }

        /// <summary>关闭时释放 WebView2（避免 msedgewebview2 进程残留）</summary>
        public void Shutdown()
        {
            try { if (_browser != null) { _browser.Dispose(); _browser = null; } } catch { }
        }

        /// <summary>导航到指定地址（等待 WebView2 初始化完成，避免竞态）</summary>
        public void Navigate(string url)
        {
            if (string.IsNullOrEmpty(url)) return;
            _addr.Text = url;
            NavigateAsync(url);
        }

        private async void NavigateAsync(string url)
        {
            try
            {
                if (_browser == null) Initialize();
                if (_browser != null && _browser.CoreWebView2 == null)
                {
                    await _browser.EnsureCoreWebView2Async();
                }
                if (_browser != null && _browser.CoreWebView2 != null)
                {
                    _browser.CoreWebView2.Navigate(url);
                }
            }
            catch { }
        }

        /// <summary>默认下载目录（文档\NtoNTier下载，可在 Config 覆盖）</summary>
        public static string DefaultDownloadDir()
        {
            var cfg = AppConfig.Load();
            if (!string.IsNullOrEmpty(cfg.DownloadDir))
            {
                try { Directory.CreateDirectory(cfg.DownloadDir); return cfg.DownloadDir; }
                catch { }
            }
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NtoNTier下载");
            try { Directory.CreateDirectory(dir); } catch { }
            return dir;
        }

        // ================= UI =================

        private void BuildUi()
        {
            var t = Theme.Current;

            // 顶部地址栏
            var bar = new Panel();
            bar.Dock = DockStyle.Top;
            bar.Height = 44;
            bar.BackColor = t.WindowBg;
            Controls.Add(bar);

            int x = 8;
            _btnBack = MakeToolBtn("◀", bar, ref x);
            _btnBack.Click += (s, e) => { if (IsReady) _browser.GoBack(); };
            _btnForward = MakeToolBtn("▶", bar, ref x);
            _btnForward.Click += (s, e) => { if (IsReady) _browser.GoForward(); };
            _btnRefresh = MakeToolBtn("⟳", bar, ref x);
            _btnRefresh.Click += (s, e) => { if (IsReady) _browser.Reload(); };

            _addr = new RoundedTextBox();
            _addr.Placeholder = "输入 http://虚拟IP:端口 或任意网址，回车访问";
            _addr.Location = new Point(x + 6, 5);
            _addr.Size = new Size(430, 34);
            _addr.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { Go(); e.Handled = true; e.SuppressKeyPress = true; } };
            bar.Controls.Add(_addr);

            _btnGo = new FlatButton();
            _btnGo.Text = "前往";
            _btnGo.Kind = FlatButton.BtnKind.Soft;
            _btnGo.Location = new Point(x + 6 + 430 + 8, 5);
            _btnGo.Size = new Size(64, 34);
            _btnGo.Click += (s, e) => Go();
            bar.Controls.Add(_btnGo);
            // 地址栏自适应宽度：窗口缩放时 _addr 撑满，_btnGo 靠右
            int addrLeft = x + 6;
            bar.Resize += (s, e) =>
            {
                if (_addr == null || _btnGo == null || bar.ClientSize.Width < 100) return;
                int goW = 64;
                int rightPad = 8;
                _addr.Width = bar.ClientSize.Width - addrLeft - goW - 8 - rightPad;
                _btnGo.Left = bar.ClientSize.Width - goW - rightPad;
            };

            // 底部下载任务面板
            var panel = new RoundedPanel();
            panel.Dock = DockStyle.Bottom;
            panel.Height = 200;
            panel.ShowBorder = true;
            panel.Fill = t.CardBg;
            Controls.Add(panel);

            var lbl = new Label();
            lbl.Text = "下载任务（分片多线程 · IDM 式）";
            lbl.Font = new Font("Microsoft YaHei UI", 9f, FontStyle.Bold);
            lbl.ForeColor = t.TextDim;
            lbl.Location = new Point(14, 10);
            lbl.AutoSize = true;
            panel.Controls.Add(lbl);

            _btnCancel = new FlatButton();
            _btnCancel.Text = "暂停/继续";
            _btnCancel.Kind = FlatButton.BtnKind.Ghost;
            _btnCancel.Location = new Point(250, 6);
            _btnCancel.Size = new Size(96, 26);
            _btnCancel.Click += (s, e) => PauseResumeSelected();
            panel.Controls.Add(_btnCancel);

            _taskList = new ListView();
            _taskList.View = View.Details;
            _taskList.FullRowSelect = true;
            _taskList.GridLines = false;
            _taskList.BorderStyle = BorderStyle.None;
            _taskList.BackColor = t.CardBg;
            _taskList.ForeColor = t.Text;
            _taskList.Columns.Add("文件", 220);
            _taskList.Columns.Add("大小", 80);
            _taskList.Columns.Add("进度", 100);
            _taskList.Columns.Add("速度", 80);
            _taskList.Columns.Add("线程/分片", 100);
            _taskList.Columns.Add("状态", 80);
            _taskList.Location = new Point(14, 40);
            _taskList.Size = new Size(680, 146);
            panel.Controls.Add(_taskList);

            // 右键菜单
            _taskMenu = new ContextMenuStrip();
            _taskMenu.Renderer = new ToolStripProfessionalRenderer(new ToolStripColorTable());
            _miPauseResume = new ToolStripMenuItem("暂停");
            _miPauseResume.Click += (s, e) => PauseResumeSelected();
            _taskMenu.Items.Add(_miPauseResume);
            _miRestart = new ToolStripMenuItem("重新下载");
            _miRestart.Click += (s, e) => RestartSelected();
            _taskMenu.Items.Add(_miRestart);
            _taskMenu.Items.Add(new ToolStripSeparator());
            _miDelete = new ToolStripMenuItem("删除");
            _miDelete.Click += (s, e) => DeleteSelected();
            _taskMenu.Items.Add(_miDelete);
            _taskMenu.Items.Add(new ToolStripSeparator());
            _miOpenFile = new ToolStripMenuItem("打开文件");
            _miOpenFile.Click += (s, e) => OpenSelectedFile();
            _taskMenu.Items.Add(_miOpenFile);
            _miOpenFolder = new ToolStripMenuItem("打开文件夹");
            _miOpenFolder.Click += (s, e) => OpenSelectedFolder();
            _taskMenu.Items.Add(_miOpenFolder);
            _taskMenu.Opening += (s, e) => UpdateTaskMenu();
            _taskList.ContextMenuStrip = _taskMenu;
        }

        /// <summary>右键菜单配色（暗色主题）</summary>
        private class ToolStripColorTable : ProfessionalColorTable
        {
            public override Color MenuItemSelected { get { return Color.FromArgb(39, 43, 52); } }
            public override Color MenuItemSelectedGradientBegin { get { return Color.FromArgb(39, 43, 52); } }
            public override Color MenuItemSelectedGradientEnd { get { return Color.FromArgb(39, 43, 52); } }
            public override Color MenuBorder { get { return Color.FromArgb(46, 50, 60); } }
            public override Color ToolStripDropDownBackground { get { return Color.FromArgb(30, 33, 39); } }
            public override Color ImageMarginGradientBegin { get { return Color.FromArgb(30, 33, 39); } }
            public override Color ImageMarginGradientMiddle { get { return Color.FromArgb(30, 33, 39); } }
            public override Color ImageMarginGradientEnd { get { return Color.FromArgb(30, 33, 39); } }
            public override Color SeparatorDark { get { return Color.FromArgb(46, 50, 60); } }
            public override Color SeparatorLight { get { return Color.FromArgb(46, 50, 60); } }
        }

        private FlatButton MakeToolBtn(string text, Panel bar, ref int x)
        {
            var b = new FlatButton();
            b.Text = text;
            b.Kind = FlatButton.BtnKind.Ghost;
            b.Location = new Point(x, 5);
            b.Size = new Size(34, 34);
            bar.Controls.Add(b);
            x += 42;
            return b;
        }

        private void Go()
        {
            string raw = _addr.Text.Trim();
            if (raw.Length == 0) return;
            if (!raw.StartsWith("http://") && !raw.StartsWith("https://")) raw = "http://" + raw;
            Navigate(raw);
        }

        // ================= 事件 =================

        private void OnNavStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            try
            {
                string u = e.Uri;
                if (string.IsNullOrEmpty(u) || u == "about:blank") return;
                if (_addr != null && _addr.Text != u) _addr.Text = u;

                // 拦截 HFS 文件链接：点击文件时强制下载，不在 WebView2 中预览
                // 判断依据：http(s) 链接 + 路径最后一段含扩展名 + 非 API 路径 + 非文件夹(以/结尾)
                if (u.StartsWith("http://") || u.StartsWith("https://"))
                {
                    try
                    {
                        var uri = new Uri(u);
                        string path = uri.AbsolutePath;
                        if (!string.IsNullOrEmpty(path) && !path.EndsWith("/") && !path.Contains("/~/"))
                        {
                            string lastSeg = Path.GetFileName(path);
                            if (!string.IsNullOrEmpty(lastSeg) && lastSeg.IndexOf('.') > 0)
                            {
                                // 是文件链接，取消导航，交给分片下载引擎
                                e.Cancel = true;
                                int threads = 16;
                                try { var cur = AppConfig.Load(); threads = cur.DownloadThreads > 0 ? cur.DownloadThreads : 4; } catch { }
                                var task = new DownloadTask(u, DefaultDownloadDir(), threads, lastSeg);
                                AddTask(task);
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        private void OnNewWindow(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            // HFS 链接 target=_blank → 改为当前页导航，避免另开窗口
            try
            {
                e.Handled = true;
                if (!string.IsNullOrEmpty(e.Uri) && _browser != null && _browser.CoreWebView2 != null)
                    _browser.CoreWebView2.Navigate(e.Uri);
            }
            catch { }
        }

        private void OnDownloadStarting(object sender, CoreWebView2DownloadStartingEventArgs e)
        {
            try
            {
                e.Cancel = true; // 立即拦截默认下载，交给分片多线程引擎
                string url = e.DownloadOperation.Uri;
                string fname = "";
                try { fname = Path.GetFileName(e.DownloadOperation.ResultFilePath); } catch { }
                if (string.IsNullOrEmpty(fname) || fname.IndexOf('.') < 0) fname = "download.bin";

                // 实时读取最新配置
                int threads = 16;
                try { var cur = AppConfig.Load(); threads = cur.DownloadThreads > 0 ? cur.DownloadThreads : 4; } catch { }
                var task = new DownloadTask(url, DefaultDownloadDir(), threads, fname);
                AddTask(task);
            }
            catch { }
        }

        // ================= 任务列表 =================

        private void AddTask(DownloadTask task)
        {
            if (_taskList.IsDisposed) return;
            if (InvokeRequired)
            {
                BeginInvoke(new Action<DownloadTask>(AddTask), task);
                return;
            }
            var item = new ListViewItem(task.FileName);
            item.SubItems.Add(FormatSize(task.TotalSize));
            item.SubItems.Add("0%");
            item.SubItems.Add("--");
            item.SubItems.Add("探测中...");
            item.SubItems.Add("排队中");
            item.Tag = task;
            _taskList.Items.Add(item);
            task.ProgressChanged += t => UpdateTask(task, item);
            // Start() 内部有 HEAD 请求（最多20秒），必须放后台线程，否则阻塞 UI
            var startThread = new System.Threading.Thread(() =>
            {
                try { task.Start(); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Download start error: " + ex.Message); }
            });
            startThread.IsBackground = true;
            startThread.Start();
        }

        private void UpdateTask(DownloadTask task, ListViewItem item)
        {
            if (item == null || item.ListView == null || item.ListView.IsDisposed) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action<DownloadTask, ListViewItem>(UpdateTask), task, item); } catch { }
                return;
            }
            double pct = task.TotalSize > 0 ? (double)task.Done / task.TotalSize * 100.0 : 0.0;
            item.SubItems[1].Text = FormatSize(task.TotalSize);
            // 进度显示百分比 + 分片完成情况
            string segInfo = "";
            if (task.SegmentsTotal > 1)
                segInfo = " (" + task.SegmentsDone + "/" + task.SegmentsTotal + "片)";
            else if (task.SegmentsTotal == 1 && task.RangeSupported)
                segInfo = " (单线程)";
            item.SubItems[2].Text = pct.ToString("0.0") + "%" + segInfo;
            item.SubItems[3].Text = task.Speed > 0 ? FormatSpeed(task.Speed) : "--";
            // 线程/分片列：显示线程数和分片模式
            string threadInfo;
            if (task.SegmentsTotal > 1)
                threadInfo = task.Threads + "线程·" + task.SegmentsTotal + "分片";
            else if (task.TotalSize > 0 && !task.RangeSupported)
                threadInfo = "单线程(不支持Range)";
            else if (task.TotalSize > 0)
                threadInfo = "单线程(小文件)";
            else
                threadInfo = "探测中...";
            item.SubItems[4].Text = threadInfo;
            item.SubItems[5].Text = StateText(task.State);
            if (task.State == DownloadState.Completed && !task.CompletedNotified)
            {
                task.CompletedNotified = true;
                if (DownloadFinished != null) DownloadFinished(task);
            }
        }

        private DownloadTask SelectedTask()
        {
            if (_taskList.SelectedItems.Count == 0) return null;
            return _taskList.SelectedItems[0].Tag as DownloadTask;
        }

        private void PauseResumeSelected()
        {
            var task = SelectedTask();
            if (task == null) return;
            if (task.State == DownloadState.Downloading) task.Pause();
            else if (task.State == DownloadState.Paused || task.State == DownloadState.Canceled || task.State == DownloadState.Failed) task.Resume();
        }

        private void RestartSelected()
        {
            var task = SelectedTask();
            if (task == null) return;
            task.Resume();
        }

        private void DeleteSelected()
        {
            if (_taskList.SelectedItems.Count == 0) return;
            var item = _taskList.SelectedItems[0];
            var task = item.Tag as DownloadTask;
            if (task != null)
            {
                task.Delete();
                _taskList.Items.Remove(item);
            }
        }

        private void OpenSelectedFile()
        {
            var task = SelectedTask();
            if (task == null || task.State != DownloadState.Completed) return;
            try { System.Diagnostics.Process.Start(task.SavePath); } catch { }
        }

        private void OpenSelectedFolder()
        {
            var task = SelectedTask();
            if (task == null) return;
            try { System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + task.SavePath + "\""); } catch { }
        }

        private void UpdateTaskMenu()
        {
            var task = SelectedTask();
            bool hasTask = task != null;
            _miPauseResume.Enabled = hasTask && (task.State == DownloadState.Downloading || task.State == DownloadState.Paused || task.State == DownloadState.Canceled || task.State == DownloadState.Failed);
            _miPauseResume.Text = (task != null && task.State == DownloadState.Downloading) ? "暂停" : "继续";
            _miRestart.Enabled = hasTask && task.State != DownloadState.Downloading && task.State != DownloadState.Completed;
            _miDelete.Enabled = hasTask;
            _miOpenFile.Enabled = hasTask && task.State == DownloadState.Completed;
            _miOpenFolder.Enabled = hasTask;
        }

        private static string StateText(DownloadState st)
        {
            switch (st)
            {
                case DownloadState.Queued: return "排队";
                case DownloadState.Paused: return "已暂停";
                case DownloadState.Downloading: return "下载中";
                case DownloadState.Completed: return "完成";
                case DownloadState.Failed: return "失败";
                case DownloadState.Canceled: return "已取消";
                default: return "";
            }
        }

        public static string FormatSize(long bytes)
        {
            if (bytes < 0) return "--";
            double b = bytes;
            if (b >= 1073741824) return (b / 1073741824).ToString("0.00") + " GB";
            if (b >= 1048576) return (b / 1048576).ToString("0.0") + " MB";
            if (b >= 1024) return (b / 1024).ToString("0") + " KB";
            return b.ToString("0") + " B";
        }

        public static string FormatSpeed(double bps)
        {
            if (bps >= 1048576) return (bps / 1048576).ToString("0.0") + " MB/s";
            if (bps >= 1024) return (bps / 1024).ToString("0") + " KB/s";
            return bps.ToString("0") + " B/s";
        }
    }
}



