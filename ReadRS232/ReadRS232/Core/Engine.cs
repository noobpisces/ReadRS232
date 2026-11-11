using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace ReadRS232.Core
{
    // =====================================================================
    //  RouteRuntime: chạy 1 tuyến  Nguồn → Tách khung → Xử lý → Đầu ra.
    // =====================================================================
    public sealed class RouteRuntime : IDisposable
    {
        public RouteConfig Config { get; }
        public string Name => Config.Name;
        public EndpointHost Source { get; }
        public Encoding Encoding => _proc.Encoding;
        public IReadOnlyList<OutputRuntime> Outputs => _outputs;

        readonly Framer _framer;
        readonly ValueProcessor _proc;
        readonly List<OutputRuntime> _outputs = new List<OutputRuntime>();
        readonly OutputRuntime[] _rawOutputs;
        readonly BlockingCollection<ValueEvent> _queue = new BlockingCollection<ValueEvent>(500);
        readonly Thread _worker;
        Timer _poll;
        long _seq;
        volatile bool _disposed;

        // ---- trạng thái cho UI ----
        public ValueEvent LastValue { get; private set; }
        public string LastResult { get; private set; } = "";
        public bool LastResultOk { get; private set; } = true;
        public DateTime LastResultTime { get; private set; }

        /// <summary>Có thay đổi trạng thái / giá trị (gọi trên thread nền).</summary>
        public event Action Changed;
        public event Action<ValueEvent> ValueProduced;

        public RouteRuntime(RouteConfig config)
        {
            Config = config;
            _proc = new ValueProcessor(config.Processing);
            _framer = new Framer(config.Processing, _proc.Encoding);
            _framer.Frame += OnFrame;

            Source = new EndpointHost(config.Source, config.Name, "Nguồn");
            Source.DataReceived += OnSourceData;
            Source.StateChanged += RaiseChanged;

            foreach (var oc in config.Outputs.Where(o => o.Enabled))
            {
                try
                {
                    var o = OutputRuntime.Create(oc, this);
                    if (o.Host != null) o.Host.StateChanged += RaiseChanged;
                    _outputs.Add(o);
                }
                catch (Exception ex) { AppLog.Write(Name, LogKind.Error, "Không tạo được đầu ra " + oc.Describe() + ": " + ex.Message); }
            }
            _rawOutputs = _outputs.Where(o => o.WantsRaw).ToArray();

            if (_proc.RegexError != null)
                AppLog.Write(Name, LogKind.Error, "Regex không hợp lệ: " + _proc.RegexError);

            _worker = new Thread(WorkerLoop) { IsBackground = true, Name = "Outputs " + Name };
            _worker.SetApartmentState(ApartmentState.STA);   // UI Automation + Clipboard cần STA
        }

        public void Start()
        {
            _worker.Start();
            foreach (var o in _outputs) o.Start();
            Source.Start();
            if (Config.PollIntervalMs > 0 && !string.IsNullOrEmpty(Config.RequestCommand))
            {
                int ms = Math.Max(50, Config.PollIntervalMs);
                _poll = new Timer(_ => { if (Source.IsOpen) SendCommand(Config.RequestCommand, force: false, quiet: true); }, null, ms, ms);
            }
        }

        // ---------------- luồng dữ liệu ----------------
        void OnSourceData(byte[] data)
        {
            AppLog.Write(Name, LogKind.Rx, null, data);
            foreach (var o in _rawOutputs) o.OnRaw(data);
            _framer.Push(data);
        }

        void OnFrame(byte[] frame)
        {
            ProcessResult r;
            try { r = _proc.Process(frame); }
            catch (Exception ex) { AppLog.Write(Name, LogKind.Error, "Lỗi xử lý: " + ex.Message); return; }

            if (!r.Ok)
            {
                if (!r.Quiet) AppLog.Write(Name, LogKind.Info, $"Bỏ qua \"{TextUtil.Visualize(r.Text)}\": {r.Reason}");
                return;
            }

            var ev = new ValueEvent
            {
                Route = Name,
                Value = r.Value,
                Number = r.Number,
                Frame = r.Text,
                FrameBytes = frame,
                ValueStart = r.ValueStart,
                ValueLength = r.ValueLength,
                Time = DateTime.Now,
                Sequence = Interlocked.Increment(ref _seq)
            };
            LastValue = ev;
            AppLog.Write(Name, LogKind.Value, ev.Value);
            ValueProduced?.Invoke(ev);
            RaiseChanged();
            Enqueue(ev);
        }

        void Enqueue(ValueEvent ev)
        {
            if (_outputs.Count == _rawOutputs.Length) return;   // không có đầu ra nào cần giá trị
            if (!_queue.TryAdd(ev))
                AppLog.Write(Name, LogKind.Warn, "Hàng đợi đầu ra đầy, bỏ giá trị " + ev.Value);
        }

        void WorkerLoop()
        {
            Native.EnsureMessageQueue();
            try
            {
                foreach (var ev in _queue.GetConsumingEnumerable())
                {
                    if (_disposed) break;
                    var parts = new List<string>();
                    bool ok = true;
                    foreach (var o in _outputs)
                    {
                        if (o.WantsRaw) continue;
                        try
                        {
                            if (o.Config.DelayMs > 0) Thread.Sleep(o.Config.DelayMs);
                            string res = o.Execute(ev);
                            parts.Add(res);
                            AppLog.Write(Name, LogKind.Out, $"{ev.Value} {res}");
                        }
                        catch (Exception ex)
                        {
                            ok = false;
                            parts.Add("✗ " + ex.Message);
                            AppLog.Write(Name, LogKind.Error, $"{o.Config.Describe()}: {ex.Message}");
                        }
                    }
                    LastResult = string.Join("  ", parts);
                    LastResultOk = ok;
                    LastResultTime = DateTime.Now;
                    RaiseChanged();
                }
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
        }

        // ---------------- thao tác từ UI ----------------
        public bool HasRequestCommand => !string.IsNullOrEmpty(Config.RequestCommand);

        /// <summary>Nút "Đọc": gửi lệnh đọc tới thiết bị; kết quả trả về luôn được gửi đi (bỏ qua lọc trùng).</summary>
        public void RequestRead()
        {
            if (HasRequestCommand) SendCommand(Config.RequestCommand, force: true, quiet: false);
            else Resend();
        }

        public void Resend()
        {
            var last = LastValue;
            if (last == null) throw new InvalidOperationException("Chưa có giá trị nào để gửi lại");
            var ev = new ValueEvent
            {
                Route = last.Route, Value = last.Value, Number = last.Number, Frame = last.Frame,
                FrameBytes = last.FrameBytes, ValueStart = last.ValueStart, ValueLength = last.ValueLength, Time = DateTime.Now, Sequence = Interlocked.Increment(ref _seq)
            };
            AppLog.Write(Name, LogKind.Info, "Gửi lại " + ev.Value);
            Enqueue(ev);
        }

        public void SendCommand(string escaped, bool force, bool quiet)
        {
            var bytes = Encoding.GetBytes(TextUtil.Unescape(escaped));
            if (force) _proc.ForceNext();
            Source.Write(bytes);
            if (!quiet) AppLog.Write(Name, LogKind.Tx, null, bytes);
        }

        /// <summary>Giả lập như thiết bị vừa gửi dữ liệu này (để thử cấu hình khi không có thiết bị).</summary>
        public void InjectReceived(string escaped)
        {
            var bytes = Encoding.GetBytes(TextUtil.Unescape(escaped));
            AppLog.Write(Name, LogKind.Info, "Giả lập nhận: " + TextUtil.Visualize(TextUtil.Unescape(escaped)));
            OnSourceData(bytes);
        }

        void RaiseChanged() { if (!_disposed) Changed?.Invoke(); }

        public void Dispose()
        {
            _disposed = true;
            _poll?.Dispose();
            _queue.CompleteAdding();
            Source.Dispose();
            _framer.Dispose();
            foreach (var o in _outputs) { try { o.Dispose(); } catch { } }
            _worker.Join(1500);
        }
    }

    // =====================================================================
    //  Engine: tập hợp các tuyến đang chạy.
    // =====================================================================
    public sealed class Engine : IDisposable
    {
        readonly List<RouteRuntime> _routes = new List<RouteRuntime>();
        public IReadOnlyList<RouteRuntime> Routes => _routes;
        public event Action RoutesChanged;

        public void Start(AppConfig cfg)
        {
            Stop();
            foreach (var rc in cfg.Routes.Where(r => r.Enabled))
            {
                var r = new RouteRuntime(rc);
                _routes.Add(r);
                r.Start();
            }
            RoutesChanged?.Invoke();
        }

        /// <summary>Khởi động lại riêng 1 tuyến (ví dụ khi đổi cổng COM ở màn hình chính).</summary>
        public RouteRuntime Restart(RouteConfig rc)
        {
            int i = _routes.FindIndex(r => r.Config.Id == rc.Id);
            if (i < 0) return null;
            _routes[i].Dispose();
            var nr = new RouteRuntime(rc);
            _routes[i] = nr;
            nr.Start();
            RoutesChanged?.Invoke();
            return nr;
        }

        /// <summary>Có thiết bị cắm/rút → thử kết nối lại ngay, không chờ hết chu kỳ.</summary>
        public void KickAll()
        {
            foreach (var r in _routes)
            {
                r.Source.Kick();
                foreach (var o in r.Outputs) o.Host?.Kick();
            }
        }

        public void Stop()
        {
            foreach (var r in _routes) { try { r.Dispose(); } catch { } }
            _routes.Clear();
        }

        public void Dispose() => Stop();
    }
}
