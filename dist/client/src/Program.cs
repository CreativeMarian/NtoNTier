using System;
using System.Windows.Forms;

namespace NtoNTier
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            // 解出内置的 edge.exe / tap-windows.exe（单文件分发）
            ResourceBootstrap.EnsureExtracted();
            var cfg = AppConfig.Load();
            Theme.Current = cfg.Dark ? Theme.Dark : Theme.Light;

            string[] args = Environment.GetCommandLineArgs();
            if (args.Length > 1 && args[1] == "--shot")
            {
                string dir = args.Length > 2 ? args[2] : System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "shots");
                try
                {
                    var f = new MainForm();
                    f.ShotMode = true;
                    f.Show();
                    Application.DoEvents();
                    System.Threading.Thread.Sleep(500);
                    f.DebugShot(dir);
                }
                catch (Exception ex)
                {
                    try { System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "error.txt"), ex.ToString()); } catch { }
                }
                Application.Exit();
                return;
            }

            // --capture <png> [w] [h]：真实屏幕抓屏（非 DrawToBitmap），用于复现用户看到的绘制残留
            if (args.Length > 1 && args[1] == "--capture")
            {
                string file = args.Length > 2 ? args[2] : "capture.png";
                int cw = args.Length > 3 ? int.Parse(args[3]) : 0;
                int ch = args.Length > 4 ? int.Parse(args[4]) : 0;
                var f = new MainForm();
                f.ShotMode = true;
                f.StartPosition = FormStartPosition.Manual;
                f.Location = new System.Drawing.Point(0, 0);
                if (cw > 0 && ch > 0) f.Size = new System.Drawing.Size(cw, ch);
                f.Show();
                f.BringToFront();
                f.TopMost = true;
                Application.DoEvents();
                // 切到亮色，匹配用户截图
                if (Theme.Current.IsDark) { f.ToggleThemePublic(); }
                // 切到自定义服务器 tab
                f.SelectServerCustom();
                var t = new System.Windows.Forms.Timer();
                t.Interval = 3000;
                t.Tick += (s, e) =>
                {
                    t.Stop();
                    try
                    {
                        var sb = new System.Text.StringBuilder();
                        DumpControls(f, sb, 0);
                        System.IO.File.WriteAllText(file + ".tree.txt", sb.ToString());
                    }
                    catch { }
                    try
                    {
                        var sc = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
                        using (var bmp = new System.Drawing.Bitmap(sc.Width, sc.Height))
                        {
                            using (var g = System.Drawing.Graphics.FromImage(bmp))
                                g.CopyFromScreen(sc.Left, sc.Top, 0, 0, bmp.Size);
                            bmp.Save(file, System.Drawing.Imaging.ImageFormat.Png);
                        }
                    }
                    catch (Exception ex)
                    {
                        try { System.IO.File.WriteAllText(file + ".error.txt", ex.ToString()); } catch { }
                    }
                    Application.Exit();
                };
                t.Start();
                Application.Run();
                return;
            }

            Application.Run(new MainForm());
        }

        static void DumpControls(System.Windows.Forms.Control c, System.Text.StringBuilder sb, int depth)
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
