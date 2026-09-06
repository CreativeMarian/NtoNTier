using System;
using System.IO;
using System.Reflection;

namespace NtoNTier
{
    /// <summary>
    /// 资源自举：edge.exe 与 tap-windows.exe 以嵌入资源打包在 NtoNTier.exe 内，
    /// 首次运行自动解出到可写目录（优先 exe 同目录，失败则 %TEMP%\NtoNTier），
    /// 实现"单文件客户端"分发。
    /// </summary>
    public static class ResourceBootstrap
    {
        public static string EdgeExePath;
        public static string TapInstallerPath;
        public static string HfsExePath;

        private static readonly string[] Embedded = { "edge.exe", "tap-windows.exe", "hfs.exe" };

        /// <summary>启动时调用：确保两个引擎文件就位</summary>
        public static void EnsureExtracted()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string targetDir = baseDir;
            // 目标目录必须可写
            if (!IsWritable(targetDir))
            {
                string tmp = Path.Combine(Path.GetTempPath(), "NtoNTier");
                try { Directory.CreateDirectory(tmp); targetDir = tmp; } catch { targetDir = baseDir; }
            }

            foreach (string name in Embedded)
            {
                string dest = Path.Combine(targetDir, name);
                // 已存在且大小正确则跳过（避免每次把 90MB hfs.exe 读入内存，优化启动速度）
                if (File.Exists(dest) && FileSize(dest) > 0) continue;
                byte[] data = ReadEmbedded(name);
                if (data == null) continue;
                try
                {
                    File.WriteAllBytes(dest, data);
                }
                catch
                {
                    // 当前目录不可写时尝试临时目录
                    try
                    {
                        string t2 = Path.Combine(Path.GetTempPath(), "NtoNTier");
                        Directory.CreateDirectory(t2);
                        dest = Path.Combine(t2, name);
                        File.WriteAllBytes(dest, data);
                        targetDir = t2;
                    }
                    catch { }
                }
            }

            EdgeExePath = Path.Combine(targetDir, "edge.exe");
            TapInstallerPath = Path.Combine(targetDir, "tap-windows.exe");
            HfsExePath = Path.Combine(targetDir, "hfs.exe");
        }

        private static long FileSize(string path)
        {
            try { return new FileInfo(path).Length; }
            catch { return -1; }
        }

        private static bool IsWritable(string dir)
        {
            try
            {
                string probe = Path.Combine(dir, ".ntowrite");
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
                    using (var ms = new MemoryStream())
                    {
                        s.CopyTo(ms);
                        return ms.ToArray();
                    }
                }
            }
            catch { return null; }
        }
    }
}
