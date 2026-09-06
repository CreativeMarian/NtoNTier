using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace NtoNTier
{
    /// <summary>
    /// P2P 高速文件传输模块。
    /// 基于 n2n 虚拟 IP 的 TCP 直连（打洞成功时不经过 supernode），
    /// 自定义轻量二进制协议（比 HTTP 开销小），多线程分片并行传输。
    /// 协议：客户端发送 "GET filename offset length\n"，服务端直接回写二进制数据。
    /// </summary>
    public static class FileTransfer
    {
        public const int DefaultPort = 8081;
        private static TcpListener _server;
        private static Thread _serverThread;
        private static volatile bool _serverRunning;
        private static string _shareDir;

        /// <summary>启动文件传输服务端</summary>
        public static string StartServer(int port, string shareDir)
        {
            if (_serverRunning) return "文件传输服务已在运行";
            try
            {
                _shareDir = shareDir;
                if (string.IsNullOrEmpty(_shareDir))
                    _shareDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "NtoNTier共享");
                try { Directory.CreateDirectory(_shareDir); } catch { }

                _server = new TcpListener(IPAddress.Any, port);
                _server.Start();
                _serverRunning = true;
                _serverThread = new Thread(ServerLoop) { IsBackground = true };
                _serverThread.Start();
                return "文件传输服务已启动 · 端口 " + port;
            }
            catch (Exception ex) { return "启动失败: " + ex.Message; }
        }

        /// <summary>停止文件传输服务端</summary>
        public static void StopServer()
        {
            _serverRunning = false;
            try { if (_server != null) _server.Stop(); } catch { }
            _server = null;
        }

        public static bool IsServerRunning { get { return _serverRunning; } }

        private static void ServerLoop()
        {
            while (_serverRunning)
            {
                try
                {
                    TcpClient client = _server.AcceptTcpClient();
                    var t = new Thread(() => HandleClient(client)) { IsBackground = true };
                    t.Start();
                }
                catch { if (!_serverRunning) break; }
            }
        }

        /// <summary>安全解析共享目录内的路径，防止 ../ 路径穿越越权读写</summary>
        private static string SafeResolve(string relativeName)
        {
            try
            {
                if (string.IsNullOrEmpty(relativeName)) return null;
                string fn = relativeName.Replace('/', Path.DirectorySeparatorChar);
                string full = Path.GetFullPath(Path.Combine(_shareDir, fn));
                string root = Path.GetFullPath(_shareDir);
                // 必须位于共享目录内
                if (!full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    && !full.Equals(root, StringComparison.OrdinalIgnoreCase))
                    return null;
                return full;
            }
            catch { return null; }
        }

        private static void HandleClient(TcpClient client)
        {
            try
            {
                using (client)
                using (var ns = client.GetStream())
                {
                    // 读取请求行: "GET filename offset length\n"
                    var sb = new StringBuilder();
                    int b;
                    while ((b = ns.ReadByte()) >= 0 && b != '\n')
                        sb.Append((char)b);
                    string req = sb.ToString().Trim();
                    if (req.StartsWith("SIZE "))
                    {
                        // SIZE 请求：返回文件大小（8字节大端整数）
                        string fn = Uri.UnescapeDataString(req.Substring(5).Trim());
                        string fp = SafeResolve(fn);
                        long size = (fp != null && File.Exists(fp)) ? new FileInfo(fp).Length : -1;
                        byte[] lenBuf = BitConverter.GetBytes(size);
                        ns.Write(lenBuf, 0, 8);
                        return;
                    }
                    if (req.StartsWith("GET "))
                    {
                        string[] parts = req.Substring(4).Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length < 3) return;

                        string fileName = Uri.UnescapeDataString(parts[0]);
                        long offset;
                        int length;
                        if (!long.TryParse(parts[1], out offset) || !int.TryParse(parts[2], out length))
                            return;
                        if (offset < 0 || length <= 0 || length > 1024 * 1024 * 1024) return;

                        string filePath = SafeResolve(fileName);
                        if (filePath == null || !File.Exists(filePath)) return;

                        using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                        {
                            fs.Seek(offset, SeekOrigin.Begin);
                            byte[] buf = new byte[65536];
                            int remaining = length;
                            while (remaining > 0)
                            {
                                int toRead = Math.Min(buf.Length, remaining);
                                int n = fs.Read(buf, 0, toRead);
                                if (n <= 0) break;
                                ns.Write(buf, 0, n);
                                remaining -= n;
                            }
                        }
                    }
                    else if (req.StartsWith("PUT "))
                    {
                        // PUT 请求（推模式）: "PUT filename size\n"，然后接收 size 字节数据
                        string[] putParts = req.Substring(4).Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (putParts.Length < 2) return;
                        string putFileName = Uri.UnescapeDataString(putParts[0]);
                        long putSize;
                        if (!long.TryParse(putParts[1], out putSize) || putSize < 0 || putSize > 1024L * 1024 * 1024 * 1024)
                            return;
                        string putPath = SafeResolve(putFileName);
                        if (putPath == null) return;
                        using (var fs = new FileStream(putPath, FileMode.Create, FileAccess.Write))
                        {
                            byte[] putBuf = new byte[131072];
                            long putRemaining = putSize;
                            while (putRemaining > 0)
                            {
                                int toRead = (int)Math.Min(putBuf.Length, putRemaining);
                                int n = ns.Read(putBuf, 0, toRead);
                                if (n <= 0) break;
                                fs.Write(putBuf, 0, n);
                                putRemaining -= n;
                            }
                        }
                        // 响应: 8字节状态（0=成功）
                        byte[] ok = BitConverter.GetBytes((long)0);
                        ns.Write(ok, 0, 8);
                    }
                }
            }
            catch { }
        }

        /// <summary>主动推送文件到对方（推模式，单线程，用于小文件或信令）</summary>
        public static bool SendFile(string ip, int port, string filePath, int timeoutMs)
        {
            try
            {
                if (!File.Exists(filePath)) return false;
                string fileName = Path.GetFileName(filePath);
                long fileSize = new FileInfo(filePath).Length;
                using (var client = new TcpClient())
                {
                    var ar = client.BeginConnect(ip, port, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(timeoutMs)) return false;
                    client.EndConnect(ar);
                    client.SendBufferSize = 262144;
                    using (var ns = client.GetStream())
                    {
                        // 发送 PUT 请求
                        string reqStr = "PUT " + Uri.EscapeDataString(fileName) + " " + fileSize + "\n";
                        byte[] req = Encoding.ASCII.GetBytes(reqStr);
                        ns.Write(req, 0, req.Length);
                        // 发送文件数据
                        using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                        {
                            byte[] buf = new byte[131072];
                            int n;
                            while ((n = fs.Read(buf, 0, buf.Length)) > 0)
                                ns.Write(buf, 0, n);
                        }
                        // 读取响应
                        byte[] resp = new byte[8];
                        int read = 0;
                        while (read < 8)
                        {
                            int n = ns.Read(resp, read, 8 - read);
                            if (n <= 0) break;
                            read += n;
                        }
                        return read >= 8 && BitConverter.ToInt64(resp, 0) == 0;
                    }
                }
            }
            catch { return false; }
        }

        /// <summary>查询对方文件大小（用于传输前确认）</summary>
        public static long QueryFileSize(string ip, int port, string fileName, int timeoutMs)
        {
            try
            {
                using (var client = new TcpClient())
                {
                    var ar = client.BeginConnect(ip, port, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(timeoutMs)) return -1;
                    client.EndConnect(ar);
                    using (var ns = client.GetStream())
                    {
                        // 发送 SIZE 请求
                        byte[] req = Encoding.ASCII.GetBytes("SIZE " + Uri.EscapeDataString(fileName) + "\n");
                        ns.Write(req, 0, req.Length);
                        // 读取8字节大小
                        byte[] lenBuf = new byte[8];
                        int read = 0;
                        while (read < 8)
                        {
                            int n = ns.Read(lenBuf, read, 8 - read);
                            if (n <= 0) break;
                            read += n;
                        }
                        if (read < 8) return -1;
                        return BitConverter.ToInt64(lenBuf, 0);
                    }
                }
            }
            catch { return -1; }
        }
    }

    /// <summary>文件传输任务状态</summary>
    public enum TransferState
    {
        Queued, Transferring, Paused, Completed, Failed, Canceled
    }

    /// <summary>P2P 文件传输任务（多线程分片，类似 IDM）</summary>
    public class TransferTask
    {
        public event Action<TransferTask> ProgressChanged;

        public string FileName { get; private set; }
        public string SavePath { get; private set; }
        public long TotalSize { get; private set; }
        public long Done { get { return Interlocked.Read(ref _doneBytes); } }
        public TransferState State { get; private set; }
        public string Error { get; private set; }
        public int Threads { get; private set; }
        public double Speed { get; private set; }
        public int SegmentsTotal { get { return _segs == null ? 0 : _segs.Count; } }
        public int SegmentsDone
        {
            get { if (_segs == null) return 0; int n = 0; foreach (var s in _segs) if (s.Done) n++; return n; }
        }

        private string _ip;
        private int _port;
        private List<Segment> _segs;
        private volatile bool _cancel;
        private volatile bool _pause;
        private long _doneBytes;
        private int _active;
        private long _lastDone;
        private DateTime _lastTick;
        private long _lastUiTick;  // UI 节流

        private class Segment
        {
            public long Start, End;
            public string PartFile;
            public volatile bool Done;
            public int RetryCount;
        }

        public TransferTask(string ip, int port, string fileName, string saveDir, int threads, long totalSize)
        {
            _ip = ip;
            _port = port;
            FileName = fileName;
            Threads = threads < 1 ? 1 : threads;
            TotalSize = totalSize;
            SavePath = Path.Combine(saveDir, fileName);
            State = TransferState.Queued;
        }

        public void Start()
        {
            if (State == TransferState.Transferring) return;
            _cancel = false;
            _pause = false;
            try { Directory.CreateDirectory(Path.GetDirectoryName(SavePath)); } catch { }
            try
            {
                BuildSegments();
                // 初始已下载字节数
                Interlocked.Exchange(ref _doneBytes, 0);
                foreach (var s in _segs)
                {
                    try
                    {
                        if (File.Exists(s.PartFile))
                        {
                            long sz = new FileInfo(s.PartFile).Length;
                            long need = s.End - s.Start + 1;
                            Interlocked.Add(ref _doneBytes, Math.Min(sz, need));
                        }
                    }
                    catch { }
                }
                State = TransferState.Transferring;
                _lastDone = 0;
                _lastTick = DateTime.UtcNow;
                _active = _segs.Count;
                Fire();
                foreach (var s in _segs)
                {
                    if (_cancel) break;
                    var t = new Thread(() => TransferSegment(s)) { IsBackground = true };
                    t.Start();
                }
            }
            catch (Exception ex)
            {
                State = TransferState.Failed;
                Error = ex.Message;
                Fire();
            }
        }

        public void Pause() { _pause = true; }
        public void Cancel() { _cancel = true; }
        public void Resume() { if (State != TransferState.Completed) Start(); }

        public void Delete()
        {
            _cancel = true;
            _pause = true;
            try { Thread.Sleep(100); } catch { }
            try
            {
                if (_segs != null) foreach (var s in _segs)
                        try { if (File.Exists(s.PartFile)) File.Delete(s.PartFile); } catch { }
                if (File.Exists(SavePath)) File.Delete(SavePath);
            }
            catch { }
            State = TransferState.Canceled;
            Error = "已删除";
            Fire();
        }

        private void BuildSegments()
        {
            _segs = new List<Segment>();
            if (TotalSize <= 0)
            {
                _segs.Add(new Segment { Start = 0, End = 0, PartFile = SavePath + ".part" });
                return;
            }
            // 动态分片：类似 IDM
            int useThreads = Threads;
            if (TotalSize < 1024 * 1024) useThreads = Math.Min(Threads, 2);
            else if (TotalSize < 10 * 1024 * 1024) useThreads = Math.Min(Threads, 4);
            else if (TotalSize < 100 * 1024 * 1024) useThreads = Math.Min(Threads, 8);

            long per = TotalSize / useThreads;
            if (per < 1) per = 1;
            long pos = 0;
            int idx = 0;
            while (pos < TotalSize)
            {
                long end = Math.Min(TotalSize - 1, pos + per - 1);
                _segs.Add(new Segment { Start = pos, End = end, PartFile = SavePath + ".part" + (idx++) });
                pos = end + 1;
            }
        }

        private void TransferSegment(Segment seg)
        {
            const int MAX_RETRY = 3;
            while (seg.RetryCount < MAX_RETRY)
            {
                if (_cancel || _pause) { seg.Done = false; OnSegDone(); return; }
                try
                {
                    long need = seg.End - seg.Start + 1;
                    long written = 0;
                    if (File.Exists(seg.PartFile)) written = new FileInfo(seg.PartFile).Length;
                    if (written > need) written = need;
                    if (written >= need)
                    {
                        seg.Done = true;
                        OnSegDone();
                        return;
                    }

                    long from = seg.Start + written;
                    int length = (int)(need - written);
                    using (var client = new TcpClient())
                    {
                        var ar = client.BeginConnect(_ip, _port, null, null);
                        if (!ar.AsyncWaitHandle.WaitOne(10000)) throw new Exception("连接超时");
                        client.EndConnect(ar);
                        client.ReceiveBufferSize = 262144;
                        using (var ns = client.GetStream())
                        {
                            // 发送请求: "GET filename offset length\n"
                            string reqStr = "GET " + Uri.EscapeDataString(FileName) + " " + from + " " + length + "\n";
                            byte[] req = Encoding.ASCII.GetBytes(reqStr);
                            ns.Write(req, 0, req.Length);

                            using (var outs = new FileStream(seg.PartFile, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read))
                            {
                                if (written > 0) outs.Seek(written, SeekOrigin.Begin);
                                byte[] buf = new byte[131072];
                                long remaining = length;
                                long sinceLastFire = 0;
                                while (remaining > 0)
                                {
                                    if (_cancel || _pause) { seg.Done = false; OnSegDone(); return; }
                                    int toRead = (int)Math.Min(buf.Length, remaining);
                                    int n = ns.Read(buf, 0, toRead);
                                    if (n <= 0) break;
                                    outs.Write(buf, 0, n);
                                    remaining -= n;
                                    Interlocked.Add(ref _doneBytes, n);
                                    sinceLastFire += n;
                                    if (sinceLastFire >= 262144) { sinceLastFire = 0; Fire(); }
                                }
                                if (remaining <= 0) { seg.Done = true; OnSegDone(); return; }
                            }
                        }
                    }
                }
                catch { }
                seg.RetryCount++;
                if (seg.RetryCount < MAX_RETRY)
                    try { Thread.Sleep(500 * seg.RetryCount); } catch { }
            }
            seg.Done = false;
            OnSegDone();
        }

        private void OnSegDone()
        {
            if (Interlocked.Decrement(ref _active) > 0) { Fire(); return; }
            Fire();
            if (_pause && !_cancel) { State = TransferState.Paused; Error = "已暂停"; Fire(); return; }
            if (_cancel) { State = TransferState.Canceled; Error = "已取消"; Fire(); return; }
            foreach (var s in _segs) if (!s.Done) { State = TransferState.Failed; Error = "部分分片传输失败"; Fire(); return; }
            try
            {
                // 预校验
                long totalPart = 0;
                foreach (var s in _segs)
                    try { if (File.Exists(s.PartFile)) totalPart += new FileInfo(s.PartFile).Length; } catch { }
                if (TotalSize > 0)
                {
                    long diff = Math.Abs(totalPart - TotalSize);
                    long tol = Math.Max(TotalSize / 100, 4096);
                    if (diff > tol) { State = TransferState.Failed; Error = "校验失败: 期望 " + TotalSize + " 实际 " + totalPart; Fire(); return; }
                }
                // 合并
                using (var fout = new FileStream(SavePath, FileMode.Create, FileAccess.Write))
                    foreach (var s in _segs)
                        using (var fin = new FileStream(s.PartFile, FileMode.Open, FileAccess.Read))
                            fin.CopyTo(fout);
                foreach (var s in _segs)
                    try { if (File.Exists(s.PartFile)) File.Delete(s.PartFile); } catch { }
                State = TransferState.Completed;
                Error = "";
            }
            catch (Exception ex) { State = TransferState.Failed; Error = "合并失败: " + ex.Message; }
            Fire();
        }

        private void Fire()
        {
            var now = DateTime.UtcNow;
            double dt = (now - _lastTick).TotalSeconds;
            if (dt >= 0.5)
            {
                long d = Done;
                if (dt > 0) Speed = (d - _lastDone) / dt;
                _lastDone = d;
                _lastTick = now;
            }
            // UI 节流：120ms 内最多触发一次；状态变化强制触发
            bool force = State == TransferState.Completed || State == TransferState.Failed
                      || State == TransferState.Paused || State == TransferState.Canceled;
            if (!force && _lastUiTick > 0 && (Environment.TickCount - _lastUiTick) < 120) return;
            _lastUiTick = Environment.TickCount;
            if (ProgressChanged != null) try { ProgressChanged(this); } catch { }
        }
    }
}
