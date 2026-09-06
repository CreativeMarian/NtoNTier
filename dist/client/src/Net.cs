using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace NtoNTier
{
    /// <summary>edge 进程管理 + 网络工具</summary>
    public static class Net
    {
        public static Process EdgeProc = null;
        public static string EdgeExePath
        {
            get { return ResourceBootstrap.EdgeExePath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "edge.exe"); }
        }
        public static string TapInstallerPath
        {
            get { return ResourceBootstrap.TapInstallerPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tap-windows.exe"); }
        }
        public static string EdgeLogPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "edge.log"); }
        }

        public static bool IsEdgeRunning
        {
            get
            {
                if (EdgeProc != null && !EdgeProc.HasExited) return true;
                // 兜底：按完整路径识别本程序的 edge.exe，避免误判其它同名进程
                return FindOurEdge() != null;
            }
        }

        public static string EdgePid
        {
            get
            {
                if (EdgeProc != null && !EdgeProc.HasExited) return EdgeProc.Id.ToString();
                var p = FindOurEdge();
                return p != null ? p.Id.ToString() : "";
            }
        }

        private static Process FindOurEdge()
        {
            string full = EdgeExePath.ToLowerInvariant();
            foreach (Process p in Process.GetProcessesByName("edge"))
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

        /// <summary>构造 edge 启动参数</summary>
        public static string[] BuildEdgeArgs(AppConfig c, string supernodeHost)
        {
            var args = new List<string>();
            args.Add("-d"); args.Add(c.TapName);
            args.Add("-c"); args.Add(c.Community);
            if (!string.IsNullOrEmpty(c.Key))
            {
                args.Add("-k"); args.Add(c.Key);
            }
            if (c.AutoIP)
            {
                args.Add("-a"); args.Add("dhcp:0.0.0.0");
            }
            else
            {
                string ip = string.Format("{0}.{1}.{2}", c.NetworkPrefix, c.Segment, c.Suffix);
                args.Add("-a"); args.Add(ip + "/" + c.Netmask);
            }
            string hostPort = supernodeHost.Contains(":") ? supernodeHost : supernodeHost + ":" + c.SupernodePort;
            args.Add("-l"); args.Add(hostPort);
            args.Add("-r");            // 开启路由，游戏间可互通
            args.Add("-E");            // 接受组播，局域网游戏需要
            args.Add("-i"); args.Add(c.RegInterval.ToString()); // 打洞保活间隔
            if (c.LocalPort > 0)
            {
                args.Add("-p"); args.Add(c.LocalPort.ToString());
            }
            if (c.MgmtPort > 0)
            {
                args.Add("-t"); args.Add(c.MgmtPort.ToString());
            }
            // 额外参数（如 -M 1200）
            if (!string.IsNullOrWhiteSpace(c.ExtraArgs))
            {
                foreach (string token in c.ExtraArgs.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    args.Add(token);
                }
            }
            return args.ToArray();
        }

        /// <summary>启动 edge（连接到 supernode 并创建虚拟网卡）</summary>
        public static string StartEdge(AppConfig c, string supernodeHost)
        {
            try
            {
                if (!File.Exists(EdgeExePath))
                {
                    return "找不到 edge.exe，请确认它与本程序在同一目录";
                }
                if (IsEdgeRunning)
                {
                    return "edge 已在运行 (PID: " + EdgePid + ")";
                }
                string[] args = BuildEdgeArgs(c, supernodeHost);
                var psi = new ProcessStartInfo(EdgeExePath);
                psi.Arguments = BuildArgsLine(args);
                psi.WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.RedirectStandardInput = true;
                // n2n edge 在中文系统下输出为 GBK，这里显式指定，避免日志中文乱码
                try
                {
                    psi.StandardOutputEncoding = System.Text.Encoding.GetEncoding(936);
                    psi.StandardErrorEncoding = System.Text.Encoding.GetEncoding(936);
                }
                catch { }
                EdgeProc = Process.Start(psi);
                Thread.Sleep(800);
                // 读取启动输出（异步，写入日志）
                EdgeProc.OutputDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) AppendEdgeLog(e.Data); };
                EdgeProc.ErrorDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) AppendEdgeLog("[err] " + e.Data); };
                EdgeProc.BeginOutputReadLine();
                EdgeProc.BeginErrorReadLine();
                if (EdgeProc.HasExited)
                {
                    return "edge 启动后立即退出，可能是虚拟网卡驱动未安装或参数错误";
                }
                return "ok|" + EdgeProc.Id;
            }
            catch (Exception ex)
            {
                return "启动失败: " + ex.Message;
            }
        }

        private static string BuildArgsLine(string[] args)
        {
            var sb = new StringBuilder();
            foreach (string a in args)
            {
                if (sb.Length > 0) sb.Append(' ');
                if (a.IndexOf(' ') >= 0) sb.Append('"').Append(a).Append('"');
                else sb.Append(a);
            }
            return sb.ToString();
        }

        private static void AppendEdgeLog(string line)
        {
            try
            {
                File.AppendAllText(EdgeLogPath, "[" + DateTime.Now.ToString("HH:mm:ss") + "] " + line + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }

        public static string StopEdge()
        {
            if (EdgeProc != null)
            {
                try { if (!EdgeProc.HasExited) EdgeProc.Kill(); } catch { }
                try { EdgeProc.WaitForExit(1500); } catch { }
                EdgeProc.Dispose();
                EdgeProc = null;
            }
            var p = FindOurEdge();
            if (p != null)
            {
                try { p.Kill(); } catch { }
                try { p.WaitForExit(1500); } catch { }
            }
            return "已断开";
        }

        /// <summary>查询 edge 管理端口，解析在线成员（只取 peer 行的 TAP 列虚拟 IP，过滤公网地址）</summary>
        public static List<string> QueryEdgeStatus(int mgmtPort, string localSubnetPrefix)
        {
            var result = new List<string>();
            try
            {
                var client = new UdpClient();
                client.Client.ReceiveTimeout = 1500;
                client.Connect(IPAddress.Loopback, mgmtPort);
                byte[] req = Encoding.ASCII.GetBytes("status\n");
                client.Send(req, req.Length);
                var ep = new IPEndPoint(IPAddress.Any, 0);
                var sbAll = new StringBuilder();
                while (true)
                {
                    try
                    {
                        byte[] buf = client.Receive(ref ep);
                        sbAll.Append(Encoding.UTF8.GetString(buf));
                    }
                    catch { break; }
                }
                client.Close();
                string text = sbAll.ToString();
                string[] lines = text.Split('\n');
                bool inPeerSection = false;   // 新格式：SUPERNODE FORWARD / PEER TO PEER 段落内
                bool hasSections = false;     // 是否检测到段落标题（新格式）
                foreach (string line in lines)
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    string trimmed = line.Trim();
                    if (trimmed.StartsWith("PEER TO PEER") || trimmed.StartsWith("SUPERNODE FORWARD")) { inPeerSection = true; hasSections = true; continue; }
                    if (trimmed.StartsWith("SUPERNODES") || trimmed.StartsWith("uptime")) { inPeerSection = false; continue; }
                    if (trimmed.StartsWith("###") || trimmed.StartsWith("COMMUNITY") || trimmed.StartsWith("=") || trimmed.StartsWith("-") || trimmed.StartsWith("Type")) continue;
                    if (hasSections && !inPeerSection) continue;   // 新格式：跳过 peer 段之外
                    // peer 行格式: 序号 | TAP_IP | MAC | EDGE地址 | ...，只取行内第一个 IPv4（TAP 列）
                    var m = System.Text.RegularExpressions.Regex.Match(trimmed, @"\b(?:\d{1,3}\.){3}\d{1,3}\b");
                    if (!m.Success) continue;
                    string ip = m.Value;
                    // 仅保留虚拟局域网网段（排除 EDGE 列的公网地址），AutoIP 无网段时保守用 10. 前缀
                    bool inSubnet = string.IsNullOrEmpty(localSubnetPrefix) ? ip.StartsWith("10.") : ip.StartsWith(localSubnetPrefix);
                    if (inSubnet && !result.Contains(ip)) result.Add(ip);
                }
            }
            catch { }
            return result;
        }

        /// <summary>从 TAP 网卡读取本机实际虚拟 IP（AutoIP 自动获取时使用）</summary>
        public static string GetTapIp(string tapName)
        {
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.Name.Equals(tapName, StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                        {
                            if (ua.Address.AddressFamily == AddressFamily.InterNetwork)
                                return ua.Address.ToString();
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        /// <summary>Ping 测试</summary>
        public static string PingTest(string host, int count, int timeout)
        {
            var sb = new StringBuilder();
            try
            {
                using (var ping = new Ping())
                {
                    for (int i = 0; i < count; i++)
                    {
                        try
                        {
                            PingReply r = ping.Send(host, timeout);
                            if (r.Status == IPStatus.Success)
                            {
                                sb.AppendLine(string.Format("第 {0} 次: {1} ms", i + 1, r.RoundtripTime));
                            }
                            else
                            {
                                sb.AppendLine(string.Format("第 {0} 次: 失败 ({1})", i + 1, r.Status));
                            }
                        }
                        catch (Exception ex)
                        {
                            sb.AppendLine(string.Format("第 {0} 次: {1}", i + 1, ex.Message));
                        }
                        if (i < count - 1) Thread.Sleep(300);
                    }
                }
            }
            catch (Exception ex) { sb.AppendLine("Ping 出错: " + ex.Message); }
            return sb.ToString();
        }

        /// <summary>TCP 测试</summary>
        public static string TcpTest(string host, int port, int timeout)
        {
            var sb = new StringBuilder();
            var sw = Stopwatch.StartNew();
            try
            {
                using (var client = new TcpClient())
                {
                    var ar = client.BeginConnect(host, port, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(timeout))
                    {
                        sb.AppendLine(string.Format("连接超时（{0} ms），端口 {1} 未开放或不可达", timeout, port));
                        return sb.ToString();
                    }
                    client.EndConnect(ar);
                    sw.Stop();
                    sb.AppendLine(string.Format("TCP 连接成功 → {0}:{1}，用时 {2} ms", host, port, sw.ElapsedMilliseconds));
                    byte[] buf = new byte[1];
                    client.Client.SendTimeout = timeout;
                    try { client.Client.Send(buf, 0); } catch { }
                }
            }
            catch (Exception ex)
            {
                sw.Stop();
                sb.AppendLine(string.Format("TCP 连接失败 → {0}:{1}：{2}", host, port, ex.Message));
            }
            return sb.ToString();
        }

        /// <summary>UDP 测试（单向发送探测，报告本地绑定信息）</summary>
        public static string UdpTest(string host, int port, int count, int timeout)
        {
            var sb = new StringBuilder();
            int sent = 0;
            var sw = Stopwatch.StartNew();
            try
            {
                using (var client = new UdpClient())
                {
                    client.Client.SendTimeout = timeout;
                    var ep = new IPEndPoint(IPAddress.Parse(Resolve(host)), port);
                    string msg = "NtoNTier UDP probe #" + Guid.NewGuid().ToString("N");
                    for (int i = 0; i < count; i++)
                    {
                        byte[] data = Encoding.UTF8.GetBytes(msg + " seq=" + i);
                        client.Send(data, data.Length, ep);
                        sent++;
                    }
                    sw.Stop();
                    sb.AppendLine(string.Format("已发送 {0} 个 UDP 包 → {1}:{2}（用时 {3} ms）", sent, host, port, sw.ElapsedMilliseconds));
                    sb.AppendLine("说明: UDP 无应答机制，若目标主机的该端口有程序监听，则可收到上述探测包。");
                }
            }
            catch (Exception ex)
            {
                sb.AppendLine("UDP 发送失败: " + ex.Message);
            }
            return sb.ToString();
        }

        private static string Resolve(string host)
        {
            IPAddress ip;
            if (IPAddress.TryParse(host, out ip)) return host;
            var addrs = Dns.GetHostAddresses(host);
            foreach (var a in addrs)
            {
                if (a.AddressFamily == AddressFamily.InterNetwork) return a.ToString();
            }
            return host;
        }

        /// <summary>安装 TAP 驱动，并确保网卡名为配置名（edge 靠名字找网卡）</summary>
        public static string InstallTap()
        {
            if (!File.Exists(TapInstallerPath)) return "找不到 tap-windows.exe";
            string r = RunAsAdmin(TapInstallerPath, "/S", "安装 TAP-Windows 虚拟网卡驱动");
            // 等待驱动/网卡就绪
            for (int i = 0; i < 15; i++)
            {
                System.Threading.Thread.Sleep(500);
                if (TapExists("")) break;
            }
            return r + Environment.NewLine + EnsureTapAdapter(TapNameFromConfig());
        }

        /// <summary>卸载 TAP 驱动</summary>
        public static string UninstallTap()
        {
            if (!File.Exists(TapInstallerPath)) return "找不到 tap-windows.exe";
            return RunAsAdmin(TapInstallerPath, "/S /remove", "卸载 TAP-Windows 虚拟网卡驱动");
        }

        /// <summary>当前配置的网卡名（取全局 AppConfig）</summary>
        public static string TapNameFromConfig()
        {
            try
            {
                var c = AppConfig.Load();
                if (!string.IsNullOrEmpty(c.TapName)) return c.TapName;
            }
            catch { }
            return "n2n_tap";
        }

        /// <summary>把已有 TAP 网卡改名为指定名称（edge 依赖网卡名）</summary>
        public static string EnsureTapAdapter(string tapName)
        {
            if (string.IsNullOrEmpty(tapName)) tapName = "n2n_tap";
            try
            {
                var searcher = new System.Management.ManagementObjectSearcher(
                    "SELECT * FROM Win32_NetworkAdapter WHERE NetConnectionID IS NOT NULL");
                foreach (System.Management.ManagementObject o in searcher.Get())
                {
                    string desc = Convert.ToString(o["Description"]);
                    if (desc != null && desc.IndexOf("TAP", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        string nc = Convert.ToString(o["NetConnectionID"]);
                        if (nc != null && !nc.Equals(tapName, StringComparison.OrdinalIgnoreCase))
                        {
                            o["NetConnectionID"] = tapName;
                            o.Put();
                            return "已把 TAP 网卡命名为 \"" + tapName + "\"";
                        }
                        return "TAP 网卡已就绪（" + tapName + "）";
                    }
                }
                return "未找到 TAP 网卡，请先安装驱动";
            }
            catch (Exception ex)
            {
                return "命名 TAP 网卡失败: " + ex.Message;
            }
        }

        /// <summary>检查 TAP 网卡是否存在</summary>
        public static bool TapExists(string tapName)
        {
            try
            {
                var searcher = new System.Management.ManagementObjectSearcher(
                    "SELECT Name, NetConnectionID, Description FROM Win32_NetworkAdapter WHERE NetConnectionID IS NOT NULL");
                foreach (var o in searcher.Get())
                {
                    string nc = Convert.ToString(o["NetConnectionID"]);
                    string desc = Convert.ToString(o["Description"]);
                    bool isTap = desc != null && desc.IndexOf("TAP", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (isTap)
                    {
                        if (string.IsNullOrEmpty(tapName)) return true;               // 只要存在任意 TAP
                        if (nc != null && nc.Equals(tapName, StringComparison.OrdinalIgnoreCase)) return true; // 指定名
                    }
                }
            }
            catch { }
            return false;
        }

        /// <summary>配置防火墙放行：服务器端口 + 本机打洞端口 + ICMP(Ping)</summary>
        public static string SetFirewall(int serverPort, int localPort)
        {
            return SetFirewall(serverPort, localPort, 0);
        }

        /// <summary>配置防火墙放行（含 HFS 文件共享端口）</summary>
        public static string SetFirewall(int serverPort, int localPort, int hfsPort)
        {
            RunNetsh("advfirewall firewall delete rule name=\"NtoNTier Supernode UDP\"");
            RunNetsh("advfirewall firewall delete rule name=\"NtoNTier Supernode TCP\"");
            RunNetsh("advfirewall firewall delete rule name=\"NtoNTier Edge UDP\"");
            RunNetsh("advfirewall firewall delete rule name=\"NtoNTier ICMPv4\"");
            RunNetsh("advfirewall firewall delete rule name=\"NtoNTier ICMPv6\"");
            RunNetsh("advfirewall firewall delete rule name=\"NtoNTier HFS TCP\"");
            RunNetsh(string.Format("advfirewall firewall add rule name=\"NtoNTier Supernode UDP\" dir=in action=allow protocol=UDP localport={0} profile=any", serverPort));
            RunNetsh(string.Format("advfirewall firewall add rule name=\"NtoNTier Supernode TCP\" dir=in action=allow protocol=TCP localport={0} profile=any", serverPort));
            if (localPort > 0 && localPort != serverPort)
            {
                RunNetsh(string.Format("advfirewall firewall add rule name=\"NtoNTier Edge UDP\" dir=in action=allow protocol=UDP localport={0} profile=any", localPort));
            }
            // ICMP Echo Request：允许虚拟局域网内其他成员 Ping 通本机
            RunNetsh("advfirewall firewall add rule name=\"NtoNTier ICMPv4\" dir=in action=allow protocol=ICMPv4 profile=any");
            RunNetsh("advfirewall firewall add rule name=\"NtoNTier ICMPv6\" dir=in action=allow protocol=ICMPv6 profile=any");
            if (hfsPort > 0)
            {
                RunNetsh(string.Format("advfirewall firewall add rule name=\"NtoNTier HFS TCP\" dir=in action=allow protocol=TCP localport={0} profile=any", hfsPort));
            }
            return "防火墙已放行：服务器端口 " + serverPort
                + (localPort > 0 && localPort != serverPort ? "，本机打洞端口 " + localPort : "")
                + (hfsPort > 0 ? "，文件共享端口 " + hfsPort : "")
                + "，ICMP(Ping)（所有网络配置文件）";
        }

        /// <summary>单独放行 HFS 文件共享端口（启动 HFS 时调用）</summary>
        public static string AllowHfsPort(int hfsPort)
        {
            if (hfsPort <= 0 || hfsPort > 65535) return "";
            RunNetsh("advfirewall firewall delete rule name=\"NtoNTier HFS TCP\"");
            RunNetsh(string.Format("advfirewall firewall add rule name=\"NtoNTier HFS TCP\" dir=in action=allow protocol=TCP localport={0} profile=any", hfsPort));
            return "已放行文件共享端口 " + hfsPort;
        }

        /// <summary>
        /// 查询 edge 管理端口，检测 supernode 连接状态。
        /// 返回 true = supernode 已响应（注册成功），false = supernode not responding。
        /// </summary>
        public static bool QuerySupernodeReachable(int mgmtPort)
        {
            try
            {
                using (var c = new UdpClient("127.0.0.1", mgmtPort))
                {
                    c.Client.ReceiveTimeout = 1200;
                    byte[] req = Encoding.ASCII.GetBytes("status\n");
                    c.Send(req, req.Length);
                    var ep = new IPEndPoint(IPAddress.Any, 0);
                    var sb = new StringBuilder();
                    while (true)
                    {
                        try
                        {
                            byte[] buf = c.Receive(ref ep);
                            sb.Append(Encoding.UTF8.GetString(buf));
                        }
                        catch { break; }
                    }
                    string s = sb.ToString();
                    if (string.IsNullOrEmpty(s)) return false;
                    // edge status 输出格式：
                    //   SUPERNODES
                    //       1. | | 119.6.178.106:10088 | | |
                    //   last_super 123 sec ago | last_p2p 45 sec ago
                    // 判断依据：last_super 秒数。若 > 60 秒说明 supernode 久未响应。
                    foreach (string raw in s.Split('\n'))
                    {
                        string t = raw.Trim();
                        if (t.StartsWith("last_super"))
                        {
                            // 格式: last_super 123 sec ago | ...
                            string[] parts = t.Split(new char[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 2)
                            {
                                long sec;
                                if (long.TryParse(parts[1], out sec))
                                {
                                    return sec < 60;  // 60秒内有响应 = 可达
                                }
                            }
                            return false;
                        }
                    }
                    // 没有 last_super 行，保守返回 false
                    return false;
                }
            }
            catch { return false; }
        }

        /// <summary>读取 edge 运行日志（用于"查看日志"）</summary>
        public static string GetEdgeLog()
        {
            try
            {
                if (!File.Exists(EdgeLogPath)) return "暂无日志（edge 尚未运行或未产生输出）";
                string text = File.ReadAllText(EdgeLogPath, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(text)) return "日志为空";
                // 只返回最近 200 行
                string[] lines = text.Split(new char[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                int skip = Math.Max(0, lines.Length - 200);
                var sb = new StringBuilder();
                for (int i = skip; i < lines.Length; i++) sb.AppendLine(lines[i].TrimEnd('\r'));
                return sb.ToString();
            }
            catch (Exception ex)
            {
                return "读取日志失败: " + ex.Message;
            }
        }

        /// <summary>当前是否以管理员权限运行</summary>
        public static bool IsAdmin
        {
            get
            {
                try
                {
                    var id = System.Security.Principal.WindowsIdentity.GetCurrent();
                    var p = new System.Security.Principal.WindowsPrincipal(id);
                    return p.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
                }
                catch { return false; }
            }
        }

        private static void RunNetsh(string args)
        {
            try
            {
                var psi = new ProcessStartInfo("netsh", args);
                psi.UseShellExecute = true;
                if (!IsAdmin) psi.Verb = "runas";
                psi.CreateNoWindow = true;
                var p = Process.Start(psi);
                if (p != null) p.WaitForExit(5000);
            }
            catch { }
        }

        private static string RunAsAdmin(string exe, string args, string note)
        {
            try
            {
                var psi = new ProcessStartInfo(exe, args);
                psi.UseShellExecute = true;
                if (!IsAdmin) psi.Verb = "runas";   // 已提权则直接运行，避免重复 UAC
                psi.CreateNoWindow = true;
                Process.Start(psi);
                return "已执行: " + note;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return "操作已取消（未授予管理员权限）";
            }
            catch (Exception ex)
            {
                return "执行失败: " + ex.Message;
            }
        }

        /// <summary>并行 Ping 探测整个虚拟网段，返回在线主机 IP（弥补 edge peer 表只显示最近活跃 peer 的问题）</summary>
        public static List<string> ScanOnlineMembers(string subnetPrefix, int timeoutMs)
        {
            var found = new List<string>();
            try
            {
                var lockObj = new object();
                var sem = new System.Threading.SemaphoreSlim(64);
                var cts = new System.Threading.CancellationTokenSource();
                cts.CancelAfter(2500);  // 2.5秒后自动取消所有 Ping，避免任务泄漏
                var tasks = new List<System.Threading.Tasks.Task>();
                for (int i = 1; i <= 254; i++)
                {
                    string ip = subnetPrefix + i;
                    tasks.Add(System.Threading.Tasks.Task.Factory.StartNew(() =>
                    {
                        if (cts.IsCancellationRequested) return;
                        sem.Wait(cts.Token);
                        try
                        {
                            if (cts.IsCancellationRequested) return;
                            using (var p = new Ping())
                            {
                                PingReply reply = p.Send(ip, timeoutMs);
                                if (reply != null && reply.Status == IPStatus.Success)
                                {
                                    lock (lockObj) { if (!found.Contains(ip)) found.Add(ip); }
                                }
                            }
                        }
                        catch { }
                        finally { try { sem.Release(); } catch { } }
                    }, cts.Token));
                }
                // 最多等 2.2 秒，CancellationToken 会在 2.5 秒强制取消剩余任务
                System.Threading.Tasks.Task.WaitAll(tasks.ToArray(), 2200);
                try { cts.Dispose(); } catch { }
                found.Sort(delegate(string a, string b) { return IpToLong(a).CompareTo(IpToLong(b)); });
            }
            catch { }
            return found;
        }

        private static long IpToLong(string ip)
        {
            try
            {
                string[] p = ip.Split('.');
                return (long.Parse(p[0]) << 24) + (long.Parse(p[1]) << 16) + (long.Parse(p[2]) << 8) + long.Parse(p[3]);
            }
            catch { return 0; }
        }
        /// <summary>链接模式：打洞直连 / 服务器中继（来自 edge 管理端口统计）</summary>
        public class LinkStatus
        {
            public bool QueryOk = false;
            public int P2pPeers = 0;    // 直连 peer 数（PEER TO PEER 段）
            public int RelayPeers = 0;  // 中继 peer 数（SUPERNODE FORWARD 段）
        }

        /// <summary>查询 edge 管理端口（UDP），解析直连 / 中继 peer 数</summary>
        public static LinkStatus QueryLinkStatus(int mgmtPort)
        {
            var r = new LinkStatus();
            try
            {
                using (var c = new UdpClient("127.0.0.1", mgmtPort))
                {
                    c.Client.ReceiveTimeout = 800;
                    byte[] nl2 = Encoding.ASCII.GetBytes("\n");
                    c.Send(nl2, nl2.Length);
                    var sb = new StringBuilder();
                    while (true)
                    {
                        try
                        {
                            var ep = new IPEndPoint(IPAddress.Any, 0);
                            byte[] buf = c.Receive(ref ep);
                            sb.Append(Encoding.UTF8.GetString(buf));
                        }
                        catch { break; }
                    }
                    string s = sb.ToString();
                    if (string.IsNullOrEmpty(s)) return r;
                    r.QueryOk = true;
                    bool inForward = false, inP2p = false;
                    foreach (string raw in s.Split('\n'))
                    {
                        string t = raw.Trim();
                        if (t.StartsWith("SUPERNODE FORWARD")) { inForward = true; inP2p = false; continue; }
                        if (t.StartsWith("PEER TO PEER")) { inP2p = true; inForward = false; continue; }
                        if (t.StartsWith("SUPERNODES")) { inForward = false; inP2p = false; continue; }
                        if (inForward && IsPeerLine(t)) r.RelayPeers++;
                        if (inP2p && IsPeerLine(t)) r.P2pPeers++;
                    }
                }
            }
            catch { }
            return r;
        }

        private static bool IsPeerLine(string line)
        {
            if (line.Length == 0 || !char.IsDigit(line[0])) return false;
            return line.IndexOf('|') >= 0;
        }
    }
}

