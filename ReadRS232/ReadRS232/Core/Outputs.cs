using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace ReadRS232.Core
{
    // =====================================================================
    //  Bản chạy của từng loại đầu ra. Execute() chạy trên thread STA riêng
    //  của tuyến, lần lượt theo thứ tự, nên app đích không bị điền chồng.
    // =====================================================================
    public abstract class OutputRuntime : IDisposable
    {
        public OutputConfig Config { get; }
        protected RouteRuntime Route { get; }
        protected readonly OutputTemplate Template;

        protected OutputRuntime(OutputConfig c, RouteRuntime route)
        {
            Config = c; Route = route;
            Template = new OutputTemplate(c.Template);
        }

        public virtual bool WantsRaw => false;
        public virtual EndpointHost Host => null;
        public virtual void Start() { }
        public virtual void OnRaw(byte[] data) { }

        /// <summary>Trả về mô tả ngắn khi thành công; ném exception khi lỗi.</summary>
        public abstract string Execute(ValueEvent ev);

        public virtual void Dispose() { }

        public static OutputRuntime Create(OutputConfig c, RouteRuntime route)
        {
            switch (c)
            {
                case WindowOutputConfig w: return new WindowOutput(w, route);
                case KeyboardOutputConfig k: return new KeyboardOutput(k, route);
                case ForwardOutputConfig f: return new ForwardOutput(f, route);
                case ClipboardOutputConfig cb: return new ClipboardOutput(cb, route);
                case FileOutputConfig fo: return new FileOutput(fo, route);
                case HttpOutputConfig h: return new HttpOutput(h, route);
                default: throw new NotSupportedException(c.GetType().Name);
            }
        }
    }

    sealed class WindowOutput : OutputRuntime
    {
        readonly WindowOutputConfig _c;
        public WindowOutput(WindowOutputConfig c, RouteRuntime r) : base(c, r) { _c = c; }
        public override string Execute(ValueEvent ev) => WindowTarget.Fill(_c, Template.Render(ev));
    }

    sealed class KeyboardOutput : OutputRuntime
    {
        readonly KeyboardOutputConfig _c;
        public KeyboardOutput(KeyboardOutputConfig c, RouteRuntime r) : base(c, r) { _c = c; }
        public override string Execute(ValueEvent ev)
        {
            Native.TypeText(Template.Render(ev), _c.KeyDelayMs);
            if (_c.PressEnter) Native.PressKey(Native.VK_RETURN);
            var fg = Native.GetForegroundWindow();
            return "⌨ " + Native.ProcessNameOf(fg);
        }
    }

    sealed class ForwardOutput : OutputRuntime
    {
        readonly ForwardOutputConfig _c;
        readonly EndpointHost _host;
        public ForwardOutput(ForwardOutputConfig c, RouteRuntime r) : base(c, r)
        {
            _c = c;
            _host = new EndpointHost(c.Target, r.Name, "Đích " + c.Target.Describe());
            if (c.Bidirectional) _host.DataReceived += BackChannel;
        }

        public override EndpointHost Host => _host;
        public override bool WantsRaw => _c.Mode == ForwardMode.Raw;
        public override void Start() => _host.Start();

        public override void OnRaw(byte[] data)
        {
            if (!_host.IsOpen)   // đích chưa sẵn sàng thì bỏ, không làm nghẽn nguồn
            {
                if (!_dropWarned)
                    AppLog.Write(Route.Name, LogKind.Warn, "Không chuyển tiếp được, đích chưa mở: " + _c.Target.Describe());
                _dropWarned = true;
                return;
            }
            _dropWarned = false;
            try
            {
                _host.Write(data);
                AppLog.Write(Route.Name, LogKind.Tx, "→ " + _c.Target.Describe(), data);
            }
            catch (Exception ex) { AppLog.Write(Route.Name, LogKind.Warn, "Chuyển tiếp lỗi: " + ex.Message); }
        }
        bool _dropWarned;

        void BackChannel(byte[] data)
        {
            AppLog.Write(Route.Name, LogKind.Tx, "↩ từ " + _c.Target.Describe(), data);
            try { Route.Source.Write(data); }
            catch (Exception ex) { AppLog.Write(Route.Name, LogKind.Warn, "Không chuyển ngược về nguồn được: " + ex.Message); }
        }

        public override string Execute(ValueEvent ev)
        {
            string s = Template.Render(ev);
            _host.Write(Route.Encoding.GetBytes(s));
            return "→ " + _c.Target.Describe();
        }

        public override void Dispose() => _host.Dispose();
    }

    sealed class ClipboardOutput : OutputRuntime
    {
        public ClipboardOutput(ClipboardOutputConfig c, RouteRuntime r) : base(c, r) { }
        public override string Execute(ValueEvent ev)
        {
            string s = Template.Render(ev);
            // Clipboard có thể đang bị app khác giữ → thử lại vài lần
            for (int i = 0; ; i++)
            {
                try { Clipboard.SetDataObject(s, true, 3, 50); return "→ Clipboard"; }
                catch when (i < 3) { Thread.Sleep(60); }
            }
        }
    }

    sealed class FileOutput : OutputRuntime
    {
        readonly FileOutputConfig _c;
        readonly OutputTemplate _path;
        public FileOutput(FileOutputConfig c, RouteRuntime r) : base(c, r)
        {
            _c = c;
            _path = new OutputTemplate(Environment.ExpandEnvironmentVariables(c.Path ?? ""), false);
        }

        public override string Execute(ValueEvent ev)
        {
            string path = _path.Render(ev, SafeFileName);
            if (string.IsNullOrWhiteSpace(path)) throw new InvalidOperationException("Chưa nhập đường dẫn file");
            string dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            var sb = new StringBuilder();
            if (!File.Exists(path) && !string.IsNullOrEmpty(_c.Header))
                sb.Append(TextUtil.Unescape(_c.Header)).Append(Environment.NewLine);
            sb.Append(Template.Render(ev));
            if (_c.AppendNewLine) sb.Append(Environment.NewLine);
            File.AppendAllText(path, sb.ToString(), new UTF8Encoding(true));
            return "→ " + Path.GetFileName(path);
        }

        static string SafeFileName(string s)
        {
            foreach (char c in Path.GetInvalidFileNameChars()) s = s.Replace(c, '_');
            return s;
        }
    }

    sealed class HttpOutput : OutputRuntime
    {
        static readonly HttpClient Client = new HttpClient();
        readonly HttpOutputConfig _c;
        readonly OutputTemplate _url;

        public HttpOutput(HttpOutputConfig c, RouteRuntime r) : base(c, r)
        {
            _c = c;
            _url = new OutputTemplate(c.Url, false);
        }

        public override string Execute(ValueEvent ev)
        {
            string url = _url.Render(ev, Uri.EscapeDataString);
            var req = new HttpRequestMessage(new HttpMethod(_c.Method.ToString()), url);
            if (_c.Method != HttpVerb.GET)
                req.Content = new StringContent(Template.Render(ev), Encoding.UTF8, string.IsNullOrEmpty(_c.ContentType) ? "text/plain" : _c.ContentType);
            using (var cts = new CancellationTokenSource(Math.Max(500, _c.TimeoutMs)))
            {
                HttpResponseMessage res;
                try { res = Client.SendAsync(req, cts.Token).GetAwaiter().GetResult(); }
                catch (OperationCanceledException) { throw new TimeoutException("HTTP quá thời gian chờ"); }
                using (res)
                {
                    if (!res.IsSuccessStatusCode) throw new InvalidOperationException($"HTTP {(int)res.StatusCode} {res.ReasonPhrase}");
                    return $"→ HTTP {(int)res.StatusCode}";
                }
            }
        }
    }
}
