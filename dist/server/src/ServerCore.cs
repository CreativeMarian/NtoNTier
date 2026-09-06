using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;

namespace NtoNServerControl
{
    /// <summary>
    /// 服务端核心：supernode 进程管理（后台无窗口运行）、嵌入资源解压、
    /// 防火墙放行、开机自启（注册表 Run 键）、日志读取。
    /// 全程无任何命令行窗口弹出。
    /// </summary>
    public static class ServerCore
    {
        public static string SupernodePath;
        private static Process _proc;
        private static readonly object _lock = new object();

        public static string LogPath { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "supernode.log"); } }
        public static string ErrLogPath { get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "supernode.log.err"); } }

        /// <summary>启动时调用：确保 supernode.exe 就位（内嵌资源解出）</summary>
        public static void EnsureSupernode()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string targetDir = baseDir;
            if (!IsWritable(targetDir))
            {
                string tmp = Path.Combine(Path.GetTempPath(), "NtoNServer");
                try { Directory.CreateDirectory(tmp); targetDir = tmp; } catch { }
            }
            string dest = Path.Combine(targetDir, "supernode.exe");
            byte[] data = ReadEmbedded("supernode.exe");
            if (data != null)
            {
                try
                {
                    if (!File.Exists(dest) || new FileInfo(dest).Length != data.Length)
                        File.WriteAllBytes(dest, data);
                }
                catch
                {
                    try
                    {
                        string t2 = Path.Combine(Path.GetTempPath(), "NtoNServer");
                        Directory.CreateDirectory(t2);
                        dest = Path.Combine(t2, "supernode.exe");
                        File.WriteAllBytes(dest, data);
                        targetDir = t2;
                    }
                    catch { }
                }
            }
            SupernodePath = dest;
        }

        private static bool IsWritable(string dir)
        {
            try
            {
                string probe = Path.Combine(dir, ".ntw");
                File.WriteAllText(probe, "1");
                File.Delete(probe);
                return true;
            }
            catch { return false; }
        }

        private static byte[] ReadEmbedded(string name)
        {
            try
            {
                var asm = Assembly.GetExecutingAssembly();
                string res = null;
                foreach (string r in asm.GetManifestResourceNames())
                {
                    if (r.EndsWith(name, StringComparison.OrdinalIgnoreCase)) { res = r; break; }
                }
                if (res == null) return null;
                using (var s = asm.GetManifestResourceStream(res))
                {
                    if (s == null) return null;
                    using (var ms = new MemoryStream()) { s.CopyTo(ms); return ms.ToArray(); }
                }
            }
            catch { return null; }
        }

        /// <summary>后台无窗口启动 supernode（-p 端口，可选 -t 管理端口）</summary>
        public static bool Start(int port, int mgmtPort)
        {
            Stop();
            EnsureSupernode();
            if (!File.Exists(SupernodePath)) return false;

            string args = "-p " + port;
            if (mgmtPort > 0) args += " -t " + mgmtPort;

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = SupernodePath,
                    Arguments = args,
                    WorkingDirectory = Path.GetDirectoryName(SupernodePath),
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                _proc = Process.Start(psi);
                // 清空旧日志并重建
                try { File.WriteAllText(LogPath, ""); } catch { }
                try { File.WriteAllText(ErrLogPath, ""); } catch { }
                _proc.OutputDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) AppendLog(LogPath, e.Data); };
                _proc.ErrorDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) AppendLog(ErrLogPath, e.Data); };
                _proc.BeginOutputReadLine();
                _proc.BeginErrorReadLine();
                return true;
            }
            catch { return false; }
        }

        private static void AppendLog(string path, string line)
        {
            try
            {
                lock (_lock) File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }

        public static void Stop()
        {
            try { if (_proc != null && !_proc.HasExited) _proc.Kill(); } catch { }
            _proc = null;
            // 兜底：按进程名结束所有 supernode
            try
            {
                foreach (Process p in Process.GetProcessesByName("supernode"))
                {
                    try { p.Kill(); } catch { }
                }
            }
            catch { }
        }

        /// <summary>当前 supernode 是否在运行（优先 _proc，兜底按进程名）</summary>
        public static bool IsRunning()
        {
            try
            {
                if (_proc != null && !_proc.HasExited) return true;
            }
            catch { }
            try { return Process.GetProcessesByName("supernode").Length > 0; } catch { return false; }
        }

        /// <summary>运行中的 supernode 进程（用于显示 PID/内存）</summary>
        public static Process GetRunning()
        {
            try
            {
                if (_proc != null && !_proc.HasExited) return _proc;
            }
            catch { }
            try
            {
                Process[] ps = Process.GetProcessesByName("supernode");
                if (ps.Length > 0) return ps[0];
            }
            catch { }
            return null;
        }

        /// <summary>读取日志（默认最近 200 行；合并 stdout+stderr）</summary>
        public static string ReadLog(int maxLines = 200)
        {
            var sb = new StringBuilder();
            AppendTail(sb, LogPath, maxLines);
            AppendTail(sb, ErrLogPath, maxLines);
            string all = sb.ToString();
            if (all.Length > 0) return all;
            return "(暂无日志 — 服务尚未启动，或尚未产生输出)";
        }

        private static void AppendTail(StringBuilder sb, string path, int max)
        {
            try
            {
                if (!File.Exists(path)) return;
                // FileShare.ReadWrite：即使其他进程正持有该日志（如旧版 ps1 启动的 supernode）
                // 也能读取，避免“文件被占用”导致日志页为空。
                string[] lines;
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                using (var sr = new StreamReader(fs, Encoding.UTF8))
                {
                    string all = sr.ReadToEnd();
                    lines = all.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                }
                int start = Math.Max(0, lines.Length - max);
                for (int i = start; i < lines.Length; i++) sb.AppendLine(lines[i]);
            }
            catch { }
        }

        /// <summary>放行防火墙 UDP/TCP 端口</summary>
        public static bool ApplyFirewall(int port)
        {
            bool ok = true;
            RunNoWindow("netsh", "advfirewall firewall delete rule name=\"NtoNServer UDP\"");
            RunNoWindow("netsh", "advfirewall firewall delete rule name=\"NtoNServer TCP\"");
            ok &= RunNoWindow("netsh", "advfirewall firewall add rule name=\"NtoNServer UDP\" dir=in action=allow protocol=UDP localport=" + port);
            ok &= RunNoWindow("netsh", "advfirewall firewall add rule name=\"NtoNServer TCP\" dir=in action=allow protocol=TCP localport=" + port);
            return ok;
        }

        private static bool RunNoWindow(string file, string args)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = file,
                    Arguments = args,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using (Process p = Process.Start(psi))
                {
                    p.WaitForExit(8000);
                    return p.HasExited && p.ExitCode == 0;
                }
            }
            catch { return false; }
        }

        /// <summary>设置/取消开机自启（HKCU Run，-autostart 静默托盘启动）</summary>
        public static void SetAutoStart(bool enable)
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key == null) return;
                    if (enable)
                    {
                        string exe = Assembly.GetExecutingAssembly().Location;
                        key.SetValue("NtoNServerControl", "\"" + exe + "\" -autostart");
                    }
                    else
                    {
                        key.DeleteValue("NtoNServerControl", false);
                    }
                }
            }
            catch { }
        }

        public static bool IsAutoStartEnabled()
        {
            try
            {
                using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Run"))
                {
                    return key != null && key.GetValue("NtoNServerControl") != null;
                }
            }
            catch { return false; }
        }
    }
}
