using System;
using System.Text;
using System.Windows.Forms;

namespace NtoNServerControl
{
    static class Program
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool RedrawWindow(IntPtr hWnd, IntPtr lprcUpdate, IntPtr hrgnUpdate, uint flags);

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            ServerCore.EnsureSupernode();
            var cfg = ServerConfig.Load();
            Theme.Current = cfg.Dark ? Theme.Dark : Theme.Light;

            string[] args = Environment.GetCommandLineArgs();

            // --capture <png> [page] [theme]：真实屏幕抓屏自检（theme=0亮 1暗，默认亮），用于 UI 验证
            if (args.Length > 1 && args[1] == "--capture")
            {
                string file = args.Length > 2 ? args[2] : "capture.png";
                int page = args.Length > 3 ? int.Parse(args[3]) : 0;
                int theme = args.Length > 4 ? int.Parse(args[4]) : 0;
                var f = new MainForm();
                f.StartPosition = FormStartPosition.Manual;
                f.Location = new System.Drawing.Point(0, 0);
                f.Show();
                f.BringToFront();
                f.TopMost = true;
                Application.DoEvents();
                f.ApplyThemeCapture(theme == 1);
                f.DebugShowPage(page);
                var t = new Timer();
                t.Interval = 2600;
                t.Tick += (s, e) =>
                {
                    t.Stop();
                    try
                    {
                        // 诊断：日志路径与读取结果（仅 --capture 页 2 时输出）
                        try
                        {
                            string diag = "BaseDir=" + AppDomain.CurrentDomain.BaseDirectory + "\r\n"
                                + "LogPath=" + ServerCore.LogPath + "\r\n"
                                + "Exists=" + System.IO.File.Exists(ServerCore.LogPath) + "\r\n"
                                + "ReadLog=[" + ServerCore.ReadLog(300) + "]";
                            System.IO.File.WriteAllText(file + ".diag.txt", diag);
                        }
                        catch (Exception dex) { try { System.IO.File.WriteAllText(file + ".diag.txt", "diag-err:" + dex); } catch { } }
                    }
                    catch { }
                    try
                    {
                        var sb = new StringBuilder();
                        DumpControls(f, sb, 0);
                        System.IO.File.WriteAllText(file + ".tree.txt", sb.ToString());
                    }
                    catch { }
                    try
                    {
                        // 强制完整重绘后再抓，避免 PrintWindow 抓到的中间渲染态
                        RedrawWindow(f.Handle, IntPtr.Zero, IntPtr.Zero, 0x0001 | 0x0002 | 0x0080);
                        f.Update();
                        System.Threading.Thread.Sleep(150);
                        using (var bmp = new System.Drawing.Bitmap(f.Width, f.Height))
                        {
                            using (var g = System.Drawing.Graphics.FromImage(bmp))
                            {
                                IntPtr hdc = g.GetHdc();
                                PrintWindow(f.Handle, hdc, 2);
                                g.ReleaseHdc(hdc);
                            }
                            bmp.Save(file, System.Drawing.Imaging.ImageFormat.Png);
                        }
                    }
                    catch (Exception ex)
                    {
                        try { System.IO.File.WriteAllText(file + ".error.txt", ex.ToString()); } catch { }
                    }
                    // 注意：不能用 Application.Exit()——会被主窗体的"最小化到托盘"拦截，必须强制退出
                    Environment.Exit(0);
                };
                t.Start();
                Application.Run();
                return;
            }

            var main = new MainForm();
            if (args.Length > 1 && args[1] == "-autostart")
            {
                // 开机自启：不显示窗口，直接托盘后台守护
                main.Hide();
                Application.Run();
            }
            else if (args.Length > 1 && args[1] == "-selftest")
            {
                // 生命周期自检：Start → 确认 → Stop → 确认（运维/测试用，无窗口）
                RunSelfTest();
            }
            else
            {
                Application.Run(main);
            }
        }

        static void RunSelfTest()
        {
            var sb = new StringBuilder();
            Action<string> Log = s => { sb.AppendLine(s); System.Console.WriteLine(s); };

            try
            {
                ServerCore.EnsureSupernode();
                Log("STEP1 EnsureSupernode: " + (System.IO.File.Exists(ServerCore.SupernodePath) ? "OK " + ServerCore.SupernodePath : "FAIL"));

                ServerCore.Stop();
                Log("STEP2 Stop old: called");
                System.Threading.Thread.Sleep(800);
                Log("STEP2 after stop IsRunning=" + ServerCore.IsRunning());

                bool started = ServerCore.Start(3301, 5645);
                Log("STEP3 Start(3301,5645): " + (started ? "OK" : "FAIL"));
                System.Threading.Thread.Sleep(2000);
                bool run = ServerCore.IsRunning();
                var p = ServerCore.GetRunning();
                Log("STEP3 IsRunning=" + run + " PID=" + (p != null ? p.Id.ToString() : "null"));
                Log("STEP3 log tail:");
                Log(ServerCore.ReadLog(6));

                ServerCore.Stop();
                System.Threading.Thread.Sleep(800);
                Log("STEP4 after Stop IsRunning=" + ServerCore.IsRunning());
                Log("SELFTEST " + (ServerCore.IsRunning() ? "FAIL" : "PASS"));
            }
            catch (Exception ex)
            {
                Log("SELFTEST EXCEPTION: " + ex);
                Log("SELFTEST FAIL");
            }
            try { System.IO.File.WriteAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "selftest.txt"), sb.ToString(), Encoding.UTF8); } catch { }
        }

        static void DumpControls(System.Windows.Forms.Control c, StringBuilder sb, int depth)
        {
            string ind = new string(' ', depth * 2);
            string txt = (c.Text ?? "").Replace("\r", "\\r").Replace("\n", "\\n");
            if (txt.Length > 30) txt = txt.Substring(0, 30) + "...";
            sb.AppendLine(ind + c.GetType().Name + " '" + txt + "' Bounds=(" + c.Left + "," + c.Top + "," + c.Width + "x" + c.Height + ") Vis=" + c.Visible + " Dock=" + c.Dock);
            foreach (System.Windows.Forms.Control child in c.Controls)
                DumpControls(child, sb, depth + 1);
        }
    }
}
