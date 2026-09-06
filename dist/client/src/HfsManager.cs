using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace NtoNTier
{
    /// <summary>
    /// HFS（HTTP File Server）文件共享引擎管理。
    /// hfs.exe 以嵌入资源打包在 NtoNTier.exe 内，运行时解压并使用独立工作目录（--cwd）隔离配置，
    /// 通过环境变量 DISABLE_UPDATE=1 跳过 GitHub 更新检查（适配国内网络；HFS 3.x 已废弃 --no-central），
    /// 端口与共享目录均可配置。
    /// </summary>
    public static class HfsManager
    {
        public static Process HfsProc = null;

        public static string HfsExePath
        {
            get { return ResourceBootstrap.HfsExePath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "hfs.exe"); }
        }

        /// <summary>HFS 工作目录（config.yaml / 数据 / 日志都放这里，避免污染用户目录）</summary>
        public static string HfsWorkDir
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NtoNTier", "hfs");
                try { Directory.CreateDirectory(dir); } catch { }
                return dir;
            }
        }

        public static string HfsLogPath { get { return Path.Combine(HfsWorkDir, "hfs-console.txt"); } }

        public static bool IsHfsRunning
        {
            get
            {
                if (HfsProc != null && !HfsProc.HasExited) return true;
                return FindOurHfs() != null;
            }
        }

        public static string HfsPid
        {
            get
            {
                if (HfsProc != null && !HfsProc.HasExited) return HfsProc.Id.ToString();
                var p = FindOurHfs();
                return p != null ? p.Id.ToString() : "";
            }
        }

        private static Process FindOurHfs()
        {
            string full = HfsExePath.ToLowerInvariant();
            foreach (Process p in Process.GetProcessesByName("hfs"))
            {
                try
                {
                    if (p.MainModule != null && p.MainModule.FileName != null &&
                        p.MainModule.FileName.ToLowerInvariant() == full)
                        return p;
                }
                catch { }
            }
            return null;
        }

        /// <summary>确保 hfs.exe 已解压就位（已存在且大于 90MB 则视为可用）</summary>
        public static bool EnsureExtracted()
        {
            try
            {
                if (File.Exists(HfsExePath) && new FileInfo(HfsExePath).Length > 90000000) return true;
                byte[] data = ReadEmbedded("hfs.exe");
                if (data == null) return false;
                File.WriteAllBytes(HfsExePath, data);
                return File.Exists(HfsExePath) && new FileInfo(HfsExePath).Length > 90000000;
            }
            catch { return false; }
        }

        private static byte[] ReadEmbedded(string name)
        {
            try
            {
                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                foreach (string r in asm.GetManifestResourceNames())
                {
                    if (r.EndsWith(name, StringComparison.OrdinalIgnoreCase))
                    {
                        using (var s = asm.GetManifestResourceStream(r))
                        {
                            if (s == null) return null;
                            using (var ms = new MemoryStream()) { s.CopyTo(ms); return ms.ToArray(); }
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        /// <summary>启动 HFS 文件共享</summary>
        public static string Start(int port, string shareDir, string adminPassword)
        {
            if (IsHfsRunning) return "文件共享已在运行（PID " + HfsPid + "）";
            if (port <= 0 || port > 65535) port = 8080;
            try
            {
                if (!EnsureExtracted()) return "启动失败：hfs.exe 未就位";
                if (string.IsNullOrWhiteSpace(shareDir))
                {
                    shareDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NtoNTier共享");
                }
                try { Directory.CreateDirectory(shareDir); } catch { }
                WriteConfig(port, shareDir, adminPassword);
                var psi = new ProcessStartInfo(HfsExePath);
                // HFS 3.x：--no-central 已废弃（实测仍触发 GitHub 更新检查），
                // 改用 DISABLE_UPDATE=1 环境变量真正跳过更新检查，避免国内网络启动卡顿。
                psi.Arguments = string.Format("--cwd \"{0}\" --port {1} --consoleFile \"{2}\"",
                    HfsWorkDir, port, HfsLogPath);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.EnvironmentVariables["DISABLE_UPDATE"] = "1";
                HfsProc = Process.Start(psi);
                for (int i = 0; i < 20; i++)
                {
                    System.Threading.Thread.Sleep(250);
                    if (PortOpen("127.0.0.1", port))
                    {
                        // 自动放行防火墙：确保虚拟局域网内其他成员能访问 8080
                        try { Net.AllowHfsPort(port); } catch { }
                        return "启动成功 · 端口 " + port;
                    }
                }
                try { Net.AllowHfsPort(port); } catch { }
                return "已启动 · 端口 " + port + "（正在就绪）";
            }
            catch (Exception ex) { return "启动失败: " + ex.Message; }
        }

        /// <summary>停止 HFS 文件共享</summary>
        public static string Stop()
        {
            try
            {
                if (HfsProc != null && !HfsProc.HasExited) { try { HfsProc.Kill(); } catch { } HfsProc = null; }
                var p = FindOurHfs();
                if (p != null) { try { p.Kill(); } catch { } }
                return "已停止文件共享";
            }
            catch { return "停止失败"; }
        }

        /// <summary>生成 config.yaml：端口、共享目录、一次性管理员</summary>
        private static void WriteConfig(int port, string shareDir, string adminPassword)
        {
            try
            {
                string share = shareDir.Replace("\\", "/");
                string cfg = "port: " + port + "\r\n"
                           + "vfs:\r\n"
                           + "  source: \"" + share + "\"\r\n"
                           + "localhost_admin: true\r\n"
                           + "open_browser_at_start: false\r\n"
                           + "force_https: false\r\n";
                if (!string.IsNullOrEmpty(adminPassword))
                    cfg += "create-admin: \"" + adminPassword + "\"\r\n";
                File.WriteAllText(Path.Combine(HfsWorkDir, "config.yaml"), cfg, new UTF8Encoding(false));
            }
            catch { }
        }

        private static bool PortOpen(string host, int port)
        {
            try
            {
                using (var c = new System.Net.Sockets.TcpClient())
                {
                    var ar = c.BeginConnect(host, port, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(300)) return false;
                    c.EndConnect(ar);
                    return true;
                }
            }
            catch { return false; }
        }
    }
}

