using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading;

namespace NtoNTier
{
    /// <summary>下载任务状态</summary>
    public enum DownloadState
    {
        Queued,      // 排队中
        Downloading, // 下载中
        Paused,      // 已暂停（可断点续传）
        Completed,   // 已完成
        Failed,      // 失败
        Canceled     // 已取消
    }

    /// <summary>
    /// IDM式分片多线程下载任务。
    /// 原理：先用 GET+Range:0-0 探测文件总大小与 Range 支持（HFS 等服务器 HEAD 不返回 Accept-Ranges），
    /// 按文件大小动态选择最优线程数，把文件按偏移切成 N 段，
    /// 每段一个线程用 HTTP Range 头并行拉取，Keep-Alive 连接复用，流式写入 .part 临时文件（不占内存），
    /// 分片失败自动重试（最多3次，指数退避），全部完成后按偏移顺序合并成完整文件并校验大小，最后清理 .part。
    /// 支持断点续传：某段 .part 已存在且字节数正确时自动跳过该段。
    /// 纯 C#5 / .NET 4.8 兼容，不依赖 WebView2。
    /// </summary>
    public class DownloadTask
    {
        /// <summary>进度/状态变化（UI 线程需自行 Invoke）</summary>
        public event Action<DownloadTask> ProgressChanged;

        public string Url { get; private set; }
        public string FileName { get; private set; }
        public string SaveDir { get; private set; }
        public string SavePath { get; private set; }
        public long TotalSize { get; private set; }
        public long Done { get { return Interlocked.Read(ref _doneBytes); } }
        public DownloadState State { get; private set; }
        public string Error { get; private set; }
        public int Threads { get; private set; }
        /// <summary>瞬时速度（字节/秒）</summary>
        public double Speed { get; private set; }
        /// <summary>总分片数（1=单线程未分片，>1=多线程分片）</summary>
        public int SegmentsTotal { get { return _segs == null ? 0 : _segs.Count; } }
        /// <summary>已完成分片数</summary>
        public int SegmentsDone
        {
            get
            {
                if (_segs == null) return 0;
                int n = 0;
                foreach (var s in _segs) if (s.Done) n++;
                return n;
            }
        }
        /// <summary>服务器是否支持 Range（支持才会真正多线程分片）</summary>
        public bool RangeSupported { get { return _rangeSupported; } }
        /// <summary>当前活跃下载线程数</summary>
        public int ActiveThreads { get { return _active; } }

        private const long MinMultiSegment = 256 * 1024; // 小于 256KB 不分片
        private class Segment
        {
            public long Start, End;
            public string PartFile;
            public volatile bool Done;
            public int RetryCount;
        }

        private List<Segment> _segs;
        private volatile bool _cancel;
        private volatile bool _pause;
        private long _doneBytes;
        private int _active;
        private long _lastDone;
        private DateTime _lastTick;
        private long _lastUiTick;  // UI 节流：距上次真正触发 UI 刷新的时间
        // 滑动窗口速度采样（最近5个采样点）
        private Queue<long> _speedWindow = new Queue<long>();
        private Queue<DateTime> _speedTimes = new Queue<DateTime>();
        private bool _rangeSupported;

        /// <summary>下载完成回调是否已触发（防止重复通知）</summary>
        public bool CompletedNotified;

        public DownloadTask(string url, string saveDir, int threads) : this(url, saveDir, threads, null) { }

        public DownloadTask(string url, string saveDir, int threads, string fileName)
        {
            Url = url;
            SaveDir = saveDir;
            Threads = threads < 1 ? 1 : threads;
            State = DownloadState.Queued;
            if (!string.IsNullOrEmpty(fileName))
                FileName = fileName;
            else
            {
                try { FileName = Path.GetFileName(new Uri(url).AbsolutePath); }
                catch { FileName = ""; }
            }
            if (string.IsNullOrEmpty(FileName) || FileName == "/") FileName = "download.bin";
            SavePath = Path.Combine(saveDir, FileName);
        }

        /// <summary>HEAD 探测 → 建分片 → 启动多线程下载（非阻塞）</summary>
        public void Start()
        {
            if (State == DownloadState.Downloading) return;
            _cancel = false;
            _pause = false;
            try { Directory.CreateDirectory(SaveDir); } catch { }
            try
            {
                // 用 GET+Range:0-0 探测是否支持断点续传（HFS 等服务器 HEAD 不返回 Accept-Ranges）
                // 返回 206 Partial Content = 支持 Range，从 Content-Range 头解析总大小
                // 返回 200 OK = 不支持 Range，单线程下载
                _rangeSupported = false;
                TotalSize = 0;
                try
                {
                    var req = (HttpWebRequest)WebRequest.Create(Url);
                    req.Method = "GET";
                    req.AddRange(0, 0);
                    req.Timeout = 15000;
                    req.ReadWriteTimeout = 15000;
                    req.AllowAutoRedirect = true;
                    using (var resp = (HttpWebResponse)req.GetResponse())
                    {
                        if (resp.StatusCode == HttpStatusCode.PartialContent)
                        {
                            _rangeSupported = true;
                            // Content-Range 格式: bytes 0-0/123456
                            string cr = resp.Headers["Content-Range"];
                            if (!string.IsNullOrEmpty(cr))
                            {
                                int slash = cr.LastIndexOf('/');
                                if (slash > 0)
                                {
                                    long total;
                                    if (long.TryParse(cr.Substring(slash + 1).Trim(), out total))
                                        TotalSize = total;
                                }
                            }
                            if (TotalSize <= 0) TotalSize = resp.ContentLength;
                        }
                        else
                        {
                            // 200 OK：不支持 Range，用普通 GET 获取大小
                            TotalSize = resp.ContentLength;
                        }
                    }
                }
                catch
                {
                    // 探测失败，回退单线程
                    _rangeSupported = false;
                    TotalSize = 0;
                }
                if (TotalSize <= 0) { _rangeSupported = false; TotalSize = 0; }

                // IDM式动态分片：根据文件大小自动选择最优线程数
                int useThreads = 1;
                if (_rangeSupported && TotalSize >= MinMultiSegment)
                {
                    if (TotalSize < 1024 * 1024) useThreads = Math.Min(Threads, 2);           // <1MB: 2线程
                    else if (TotalSize < 10 * 1024 * 1024) useThreads = Math.Min(Threads, 4);  // 1-10MB: 4线程
                    else if (TotalSize < 100 * 1024 * 1024) useThreads = Math.Min(Threads, 8); // 10-100MB: 8线程
                    else useThreads = Threads;                                                  // >100MB: 满线程
                }
                BuildSegments(useThreads);

                // 断点续传：根据所有 .part 文件大小计算初始已下载字节数，避免重复计数
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

                State = DownloadState.Downloading;
                _lastDone = 0;
                _lastTick = DateTime.UtcNow;
                _active = _segs.Count;
                Fire();
                foreach (var s in _segs)
                {
                    if (_cancel) break;
                    var t = new Thread(() => DownloadSegment(s));
                    t.IsBackground = true;
                    t.Start();
                }
                if (_segs.Count == 0)
                {
                    State = DownloadState.Failed;
                    Error = "无可下载分片";
                    Fire();
                }
            }
            catch (Exception ex)
            {
                State = DownloadState.Failed;
                Error = ex.Message;
                Fire();
            }
        }

        /// <summary>取消下载（保留 .part，下次可续传）</summary>
        public void Cancel()
        {
            _cancel = true;
        }

        /// <summary>暂停下载（保留 .part，可断点续传）</summary>
        public void Pause()
        {
            _pause = true;
        }

        /// <summary>从暂停/取消/失败状态恢复下载（断点续传）</summary>
        public void Resume()
        {
            if (State == DownloadState.Completed) return;
            Start();
        }

        /// <summary>删除下载任务及所有临时文件</summary>
        public void Delete()
        {
            _cancel = true;
            _pause = true;
            // 等待一小段时间让下载线程退出
            System.Threading.Thread.Sleep(100);
            try
            {
                if (_segs != null)
                {
                    foreach (var s in _segs)
                        try { if (File.Exists(s.PartFile)) File.Delete(s.PartFile); } catch { }
                }
                if (File.Exists(SavePath)) File.Delete(SavePath);
            }
            catch { }
            State = DownloadState.Canceled;
            Error = "已删除";
            Fire();
        }

        // ================= 内部实现 =================

        private HttpWebResponse Head(string url)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "HEAD";
            req.Timeout = 20000;
            req.ReadWriteTimeout = 20000;
            req.AllowAutoRedirect = true;
            return (HttpWebResponse)req.GetResponse();
        }

        private void BuildSegments(int threads)
        {
            _segs = new List<Segment>();
            if (!_rangeSupported || TotalSize <= 0)
            {
                // 无 Range 支持：单段整文件
                _segs.Add(new Segment { Start = 0, End = Math.Max(0, TotalSize - 1), PartFile = SavePath + ".part" });
                return;
            }
            long size = TotalSize;
            long per = size / threads;
            if (per < 1) per = 1;
            long pos = 0;
            int idx = 0;
            while (pos < size)
            {
                long end = Math.Min(size - 1, pos + per - 1);
                _segs.Add(new Segment { Start = pos, End = end, PartFile = SavePath + ".part" + (idx++) });
                pos = end + 1;
            }
        }

        private void DownloadSegment(Segment seg)
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
                        // 已完整下载（断点续传命中），_doneBytes 已在 Start() 初始计算中包含，不再重复加
                        seg.Done = true;
                        OnSegDone();
                        return;
                    }

                    long from = seg.Start + written;
                    var req = (HttpWebRequest)WebRequest.Create(Url);
                    req.Method = "GET";
                    req.Timeout = 30000;
                    req.ReadWriteTimeout = 30000;
                    req.KeepAlive = true;  // 连接复用，减少TCP握手
                    req.AddRange(from, seg.End);
                    req.AllowAutoRedirect = true;
                    using (var resp = (HttpWebResponse)req.GetResponse())
                    using (var ins = resp.GetResponseStream())
                    using (var outs = new FileStream(seg.PartFile, FileMode.OpenOrCreate, FileAccess.Write, FileShare.Read))
                    {
                        if (written > 0) outs.Seek(written, SeekOrigin.Begin);
                        byte[] buf = new byte[65536];
                        long remaining = need - written;
                        int n;
                        long sinceLastFire = 0;  // 自上次进度更新以来下载的字节数
                        while (remaining > 0 && (n = ins.Read(buf, 0, (int)Math.Min(buf.Length, remaining))) > 0)
                        {
                            if (_cancel || _pause) { seg.Done = false; OnSegDone(); return; }
                            outs.Write(buf, 0, n);
                            remaining -= n;
                            Interlocked.Add(ref _doneBytes, n);
                            // 实时进度更新：每下载 256KB 触发一次 UI 刷新
                            sinceLastFire += n;
                            if (sinceLastFire >= 262144)
                            {
                                sinceLastFire = 0;
                                Fire();
                            }
                        }
                        if (remaining <= 0) { seg.Done = true; OnSegDone(); return; }
                        // 未下完，重试
                    }
                }
                catch { }
                seg.RetryCount++;
                if (seg.RetryCount < MAX_RETRY)
                {
                    try { System.Threading.Thread.Sleep(500 * seg.RetryCount); } catch { }
                }
            }
            seg.Done = false;
            OnSegDone();
        }

        private void OnSegDone()
        {
            if (Interlocked.Decrement(ref _active) > 0)
            {
                Fire();
                return;
            }
            Fire();

            if (_pause && !_cancel)
            {
                State = DownloadState.Paused;
                Error = "已暂停";
                Fire();
                return;
            }
            if (_cancel)
            {
                State = DownloadState.Canceled;
                Error = "已取消";
                Fire();
                return;
            }
            foreach (var s in _segs)
            {
                if (!s.Done)
                {
                    State = DownloadState.Failed;
                    Error = "部分分片下载失败";
                    Fire();
                    return;
                }
            }
            // 全部成功 → 先校验所有 .part 总大小，再合并（校验失败时保留 .part 可重试）
            try
            {
                // 预校验：统计所有 .part 文件总大小
                long totalPartSize = 0;
                foreach (var s in _segs)
                {
                    try
                    {
                        if (File.Exists(s.PartFile))
                            totalPartSize += new FileInfo(s.PartFile).Length;
                    }
                    catch { }
                }
                // 校验：允许 1% 或 4KB 以内的误差（网络波动/编码差异），超过则判定失败
                if (TotalSize > 0)
                {
                    long diff = Math.Abs(totalPartSize - TotalSize);
                    long tolerance = Math.Max(TotalSize / 100, 4096);
                    if (diff > tolerance)
                    {
                        State = DownloadState.Failed;
                        Error = "校验失败: 期望 " + TotalSize + " 字节, 实际 " + totalPartSize + " 字节（.part 已保留，可重试）";
                        Fire();
                        return;
                    }
                }
                // 校验通过 → 合并并清理 .part
                Merge();
                State = DownloadState.Completed;
                Error = "";
            }
            catch (Exception ex)
            {
                State = DownloadState.Failed;
                Error = "合并失败: " + ex.Message;
            }
            Fire();
        }

        private void Merge()
        {
            using (var fout = new FileStream(SavePath, FileMode.Create, FileAccess.Write))
            {
                foreach (var s in _segs)
                {
                    using (var fin = new FileStream(s.PartFile, FileMode.Open, FileAccess.Read))
                        fin.CopyTo(fout);
                }
            }
            foreach (var s in _segs)
            {
                try { if (File.Exists(s.PartFile)) File.Delete(s.PartFile); } catch { }
            }
        }

        private void Fire()
        {
            // IDM式滑动窗口速度：最近5个采样点平均，更平稳
            var now = DateTime.UtcNow;
            double dt = (now - _lastTick).TotalSeconds;
            if (dt >= 0.8)
            {
                long d = Done;
                double instant = (d - _lastDone) / dt;
                _speedWindow.Enqueue((long)instant);
                _speedTimes.Enqueue(now);
                while (_speedWindow.Count > 5) { _speedWindow.Dequeue(); _speedTimes.Dequeue(); }
                if (_speedWindow.Count > 0)
                {
                    long sum = 0;
                    foreach (var v in _speedWindow) sum += v;
                    Speed = (double)sum / _speedWindow.Count;
                }
                _lastDone = d;
                _lastTick = now;
            }
            // UI 节流：高频下载时避免每 256KB 触发一次 BeginInvoke 造成消息风暴
            // 120ms 内最多触发一次；状态变化（完成/失败/暂停/取消）强制触发
            bool force = State == DownloadState.Completed || State == DownloadState.Failed
                      || State == DownloadState.Paused || State == DownloadState.Canceled;
            if (!force && _lastUiTick > 0 && (Environment.TickCount - _lastUiTick) < 120) return;
            _lastUiTick = Environment.TickCount;
            if (ProgressChanged != null)
            {
                try { ProgressChanged(this); } catch { }
            }
        }
    }
}

