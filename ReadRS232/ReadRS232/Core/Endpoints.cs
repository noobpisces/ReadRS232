using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace ReadRS232.Core
{
    // =====================================================================
    //  Endpoint = 1 đường truyền byte 2 chiều (COM, TCP client, TCP server).
    //  Dùng chung cho cả nguồn dữ liệu và đầu ra chuyển tiếp.
    // =====================================================================
    public interface IByteEndpoint : IDisposable
    {
        event Action<byte[]> DataReceived;   // gọi trên thread nền
        event Action<string> Lost;           // mất kết nối (kèm lý do)
        event Action InfoChanged;            // ví dụ số client TCP thay đổi
        bool IsOpen { get; }
        string Info { get; }
        void Open();                         // ném exception nếu lỗi
        void Write(byte[] data);
        bool HealthCheck();                  // false = coi như đã mất kết nối
    }

    public static class EndpointFactory
    {
        public static IByteEndpoint Create(EndpointConfig c)
        {
            switch (c)
            {
                case SerialEndpointConfig s: return new SerialEndpoint(s);
                case TcpClientEndpointConfig t: return new TcpClientEndpoint(t);
                case TcpServerEndpointConfig t: return new TcpServerEndpoint(t);
                default: throw new NotSupportedException(c?.GetType().Name);
            }
        }
    }

    // ---------------------------------------------------------------------
    sealed class SerialEndpoint : IByteEndpoint
    {
        readonly SerialEndpointConfig _c;
        SerialPort _port;
        Stream _stream;
        Thread _reader;
        volatile bool _open;

        public event Action<byte[]> DataReceived;
        public event Action<string> Lost;
        public event Action InfoChanged { add { } remove { } }

        public SerialEndpoint(SerialEndpointConfig c) { _c = c; }

        public bool IsOpen => _open;
        public string Info => _c.Describe();

        public void Open()
        {
            if (string.IsNullOrWhiteSpace(_c.PortName)) throw new InvalidOperationException("Chưa chọn cổng COM");
            var p = new SerialPort(_c.PortName.Trim(), _c.BaudRate, _c.Parity, _c.DataBits, _c.StopBits)
            {
                Handshake = _c.Handshake,
                DtrEnable = _c.DtrEnable,
                RtsEnable = _c.RtsEnable,
                ReadTimeout = 300,
                WriteTimeout = 1500,
                ReadBufferSize = 64 * 1024,
                WriteBufferSize = 16 * 1024
            };
            try { p.Open(); }
            catch (UnauthorizedAccessException) { p.Dispose(); throw new IOException(_c.PortName + " đang bị app khác mở"); }
            catch (FileNotFoundException) { p.Dispose(); throw new IOException("Không thấy " + _c.PortName); }
            catch (IOException ex) when (ex.Message.Contains("does not exist") || ex.Message.Contains("không tồn tại"))
            { p.Dispose(); throw new IOException("Không thấy " + _c.PortName); }

            _port = p;
            _stream = p.BaseStream;
            // Lỗi kinh điển của .NET: rút USB-Serial làm finalizer của SerialStream ném exception và sập app.
            GC.SuppressFinalize(_stream);
            _open = true;
            _reader = new Thread(ReadLoop) { IsBackground = true, Name = "Serial " + _c.PortName };
            _reader.Start();
        }

        void ReadLoop()
        {
            var buf = new byte[8192];
            while (_open)
            {
                try
                {
                    int n = _stream.Read(buf, 0, buf.Length);
                    if (n > 0)
                    {
                        var data = new byte[n];
                        Buffer.BlockCopy(buf, 0, data, 0, n);
                        DataReceived?.Invoke(data);
                    }
                }
                catch (TimeoutException) { }
                catch (Exception ex)
                {
                    if (_open) Fail("Mất kết nối: " + ex.Message);
                    return;
                }
            }
        }

        public void Write(byte[] data)
        {
            if (!_open) throw new IOException("Cổng chưa mở");
            try { _stream.Write(data, 0, data.Length); }
            catch (TimeoutException) { throw new IOException("Ghi quá thời gian (thiết bị không nhận?)"); }
            catch (Exception ex) when (!(ex is IOException)) { Fail(ex.Message); throw new IOException(ex.Message, ex); }
        }

        public bool HealthCheck()
        {
            if (!_open) return false;
            // Driver USB-Serial (Prolific, CH340...) đôi khi không báo lỗi khi rút cáp, nên kiểm tra cổng còn tồn tại không
            try
            {
                if (!SerialPort.GetPortNames().Any(n => string.Equals(n, _c.PortName.Trim(), StringComparison.OrdinalIgnoreCase)))
                {
                    Fail("Cổng " + _c.PortName + " đã bị rút");
                    return false;
                }
            }
            catch { }
            return true;
        }

        void Fail(string reason)
        {
            if (!_open) return;
            CloseInternal();
            Lost?.Invoke(reason);
        }

        void CloseInternal()
        {
            _open = false;
            var p = _port; var s = _stream;
            _port = null; _stream = null;
            if (p == null) return;
            // Đóng cổng USB đã rút có thể treo → đóng ở thread nền, không chờ quá 2s
            var t = Task.Run(() =>
            {
                try { s?.Close(); } catch { }
                try { p.Close(); } catch { }
                try { p.Dispose(); } catch { }
            });
            t.Wait(2000);
        }

        public void Dispose() => CloseInternal();
    }

    // ---------------------------------------------------------------------
    sealed class TcpClientEndpoint : IByteEndpoint
    {
        readonly TcpClientEndpointConfig _c;
        TcpClient _client;
        NetworkStream _stream;
        volatile bool _open;

        public event Action<byte[]> DataReceived;
        public event Action<string> Lost;
        public event Action InfoChanged { add { } remove { } }

        public TcpClientEndpoint(TcpClientEndpointConfig c) { _c = c; }
        public bool IsOpen => _open;
        public string Info => _c.Describe();

        public void Open()
        {
            var cl = new TcpClient { NoDelay = true };
            var t = cl.ConnectAsync(_c.Host, _c.Port);
            try
            {
                if (!t.Wait(3000)) { cl.Close(); throw new IOException($"Hết thời gian kết nối {_c.Host}:{_c.Port}"); }
            }
            catch (AggregateException ex)
            {
                cl.Close();
                throw new IOException(ex.InnerException?.Message ?? ex.Message);
            }
            _client = cl;
            _stream = cl.GetStream();
            _open = true;
            new Thread(ReadLoop) { IsBackground = true, Name = "TCP client " + _c.Port }.Start();
        }

        void ReadLoop()
        {
            var buf = new byte[8192];
            try
            {
                while (_open)
                {
                    int n = _stream.Read(buf, 0, buf.Length);
                    if (n <= 0) { Fail("Máy kia đã đóng kết nối"); return; }
                    var d = new byte[n];
                    Buffer.BlockCopy(buf, 0, d, 0, n);
                    DataReceived?.Invoke(d);
                }
            }
            catch (Exception ex) { Fail(ex.Message); }
        }

        public void Write(byte[] data)
        {
            if (!_open) throw new IOException("Chưa kết nối");
            try { _stream.Write(data, 0, data.Length); }
            catch (Exception ex) { Fail(ex.Message); throw new IOException(ex.Message, ex); }
        }

        public bool HealthCheck() => _open;

        void Fail(string reason)
        {
            if (!_open) return;
            Dispose();
            Lost?.Invoke(reason);
        }

        public void Dispose()
        {
            _open = false;
            try { _client?.Close(); } catch { }
            _client = null;
        }
    }

    // ---------------------------------------------------------------------
    sealed class TcpServerEndpoint : IByteEndpoint
    {
        readonly TcpServerEndpointConfig _c;
        TcpListener _listener;
        readonly List<TcpClient> _clients = new List<TcpClient>();
        volatile bool _open;

        public event Action<byte[]> DataReceived;
        public event Action<string> Lost;
        public event Action InfoChanged;

        public TcpServerEndpoint(TcpServerEndpointConfig c) { _c = c; }
        public bool IsOpen => _open;

        public string Info
        {
            get
            {
                int n; lock (_clients) n = _clients.Count;
                return $"TCP server :{_c.Port} · {n} kết nối";
            }
        }

        public void Open()
        {
            IPAddress ip = IPAddress.Loopback;
            if (!string.IsNullOrWhiteSpace(_c.ListenAddress) && !IPAddress.TryParse(_c.ListenAddress.Trim(), out ip))
                throw new IOException("Địa chỉ nghe không hợp lệ: " + _c.ListenAddress);
            var l = new TcpListener(ip, _c.Port);
            l.Start();
            _listener = l;
            _open = true;
            new Thread(AcceptLoop) { IsBackground = true, Name = "TCP server " + _c.Port }.Start();
        }

        void AcceptLoop()
        {
            while (_open)
            {
                TcpClient cl;
                try { cl = _listener.AcceptTcpClient(); }
                catch (Exception ex)
                {
                    if (_open) { _open = false; Lost?.Invoke(ex.Message); }
                    return;
                }
                cl.NoDelay = true;
                lock (_clients) _clients.Add(cl);
                InfoChanged?.Invoke();
                new Thread(() => ClientLoop(cl)) { IsBackground = true, Name = "TCP peer" }.Start();
            }
        }

        void ClientLoop(TcpClient cl)
        {
            var buf = new byte[8192];
            try
            {
                var s = cl.GetStream();
                while (_open)
                {
                    int n = s.Read(buf, 0, buf.Length);
                    if (n <= 0) break;
                    var d = new byte[n];
                    Buffer.BlockCopy(buf, 0, d, 0, n);
                    DataReceived?.Invoke(d);
                }
            }
            catch { }
            Drop(cl);
        }

        void Drop(TcpClient cl)
        {
            bool removed;
            lock (_clients) removed = _clients.Remove(cl);
            try { cl.Close(); } catch { }
            if (removed) InfoChanged?.Invoke();
        }

        public void Write(byte[] data)
        {
            if (!_open) throw new IOException("Server chưa chạy");
            TcpClient[] list;
            lock (_clients) list = _clients.ToArray();
            foreach (var cl in list)
            {
                try { cl.GetStream().Write(data, 0, data.Length); }
                catch { Drop(cl); }
            }
        }

        public bool HealthCheck() => _open;

        public void Dispose()
        {
            _open = false;
            try { _listener?.Stop(); } catch { }
            TcpClient[] list;
            lock (_clients) { list = _clients.ToArray(); _clients.Clear(); }
            foreach (var cl in list) try { cl.Close(); } catch { }
        }
    }

    // =====================================================================
    //  EndpointHost: giữ 1 endpoint luôn sống — tự mở, tự kết nối lại,
    //  báo trạng thái cho UI. Mọi I/O ra ngoài đi qua đây.
    // =====================================================================
    public enum LinkState { Off, Connecting, Connected, Error }

    public sealed class EndpointHost : IDisposable
    {
        public EndpointConfig Config { get; }
        readonly string _route;
        readonly string _role;
        IByteEndpoint _ep;
        readonly AutoResetEvent _kick = new AutoResetEvent(false);
        volatile bool _running;
        Thread _thread;
        string _lastError;

        public LinkState State { get; private set; } = LinkState.Off;
        public string StateText { get; private set; } = "Chưa chạy";

        public event Action<byte[]> DataReceived;
        public event Action StateChanged;

        public EndpointHost(EndpointConfig config, string route, string role)
        {
            Config = config; _route = route; _role = role;
        }

        public bool IsOpen => _ep != null && _ep.IsOpen;

        public void Start()
        {
            if (_running) return;
            _running = true;
            _thread = new Thread(Supervise) { IsBackground = true, Name = "Host " + _role };
            _thread.Start();
        }

        /// <summary>Thử kết nối lại ngay (ví dụ khi Windows báo có thiết bị mới cắm).</summary>
        public void Kick() => _kick.Set();

        void Supervise()
        {
            bool first = true;
            while (_running)
            {
                var ep = _ep;
                if (ep == null || !ep.IsOpen)
                {
                    if (first || Config.AutoReconnect) TryOpen();
                    first = false;
                }
                else ep.HealthCheck();

                int wait = Math.Max(250, Config.ReconnectIntervalMs);
                _kick.WaitOne(IsOpen ? Math.Min(wait, 1500) : wait);
            }
        }

        void TryOpen()
        {
            CloseEndpoint();
            SetState(LinkState.Connecting, "Đang kết nối " + Config.Describe());
            IByteEndpoint ep = null;
            try
            {
                ep = EndpointFactory.Create(Config);
                ep.DataReceived += OnData;
                ep.Lost += OnLost;
                ep.InfoChanged += OnInfo;
                ep.Open();
                if (!_running) { ep.Dispose(); return; }   // bị dừng trong lúc đang mở
                _ep = ep;
                _lastError = null;
                SetState(LinkState.Connected, ep.Info);
                AppLog.Write(_route, LogKind.Info, $"{_role}: đã kết nối {ep.Info}");
            }
            catch (Exception ex)
            {
                if (ep != null) { ep.DataReceived -= OnData; ep.Lost -= OnLost; ep.InfoChanged -= OnInfo; try { ep.Dispose(); } catch { } }
                SetState(LinkState.Error, ex.Message);
                if (_lastError != ex.Message)       // không spam log khi đang chờ cắm lại
                    AppLog.Write(_route, LogKind.Warn, $"{_role}: {ex.Message}");
                _lastError = ex.Message;
            }
        }

        void OnData(byte[] d) => DataReceived?.Invoke(d);
        void OnInfo() { var ep = _ep; if (ep != null && ep.IsOpen) SetState(LinkState.Connected, ep.Info); }

        void OnLost(string reason)
        {
            _lastError = reason;
            SetState(LinkState.Error, reason);
            AppLog.Write(_route, LogKind.Warn, $"{_role}: {reason}");
            _kick.Set();
        }

        public void Write(byte[] data)
        {
            var ep = _ep;
            if (ep == null || !ep.IsOpen) throw new IOException("Chưa kết nối: " + Config.Describe());
            ep.Write(data);
        }

        void SetState(LinkState s, string text)
        {
            State = s; StateText = text;
            StateChanged?.Invoke();
        }

        void CloseEndpoint()
        {
            var ep = _ep;
            _ep = null;
            if (ep == null) return;
            ep.DataReceived -= OnData; ep.Lost -= OnLost; ep.InfoChanged -= OnInfo;
            try { ep.Dispose(); } catch { }
        }

        public void Dispose()
        {
            _running = false;
            _kick.Set();
            CloseEndpoint();
            _thread?.Join(500);
            SetState(LinkState.Off, "Đã dừng");
        }
    }
}
