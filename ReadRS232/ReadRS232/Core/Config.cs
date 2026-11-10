using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Xml.Serialization;
using ReadRS232.UI;

namespace ReadRS232.Core
{
    // =====================================================================
    //  Cấu hình của toàn app. Lưu dưới dạng XML ở %AppData%\ReadRS232\config.xml
    //  Mọi thuộc tính đều hiện trong màn hình Cài đặt (PropertyGrid) với
    //  tên/mô tả tiếng Việt, nên muốn thêm tuỳ chọn chỉ cần thêm property.
    // =====================================================================

    [XmlRoot("ReadRS232")]
    public class AppConfig
    {
        public int Version { get; set; } = 2;

        [XmlArray("Routes")]
        [XmlArrayItem("Route")]
        public List<RouteConfig> Routes { get; set; } = new List<RouteConfig>();

        public UiConfig Ui { get; set; } = new UiConfig();

        // ---------- lưu / đọc ----------

        public static string DefaultPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ReadRS232", "config.xml");

        static readonly XmlSerializer Serializer = new XmlSerializer(typeof(AppConfig));

        public static AppConfig Load(string path, out string warning)
        {
            warning = null;
            if (!File.Exists(path))
                return CreateDefault();
            try
            {
                using (var fs = File.OpenRead(path))
                {
                    var cfg = (AppConfig)Serializer.Deserialize(fs);
                    cfg.Normalize();
                    return cfg;
                }
            }
            catch (Exception ex)
            {
                // File hỏng: giữ lại bản lỗi để người dùng còn cứu được, rồi chạy cấu hình mặc định
                string bad = path + ".broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                try { File.Copy(path, bad, true); } catch { }
                warning = "Không đọc được file cấu hình, đã dùng cấu hình mặc định.\nBản lỗi được giữ ở: " + bad + "\n\n" + ex.Message;
                return CreateDefault();
            }
        }

        public void Save(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp = path + ".tmp";
            using (var fs = File.Create(tmp))
            using (var w = new StreamWriter(fs, new UTF8Encoding(false)))
                Serializer.Serialize(w, this);
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }

        public AppConfig Clone()
        {
            using (var ms = new MemoryStream())
            {
                Serializer.Serialize(ms, this);
                ms.Position = 0;
                var c = (AppConfig)Serializer.Deserialize(ms);
                c.Normalize();
                return c;
            }
        }

        void Normalize()
        {
            if (Routes == null) Routes = new List<RouteConfig>();
            if (Ui == null) Ui = new UiConfig();
            foreach (var r in Routes)
            {
                if (string.IsNullOrEmpty(r.Id)) r.Id = Guid.NewGuid().ToString("N");
                if (r.Source == null) r.Source = new SerialEndpointConfig();
                if (r.Processing == null) r.Processing = new ProcessingConfig();
                if (r.Outputs == null) r.Outputs = new List<OutputConfig>();
            }
        }

        /// <summary>Cấu hình mặc định = đúng hành vi của bản cũ (đọc cân, +13, điền vào WorkSupportSystem).</summary>
        public static AppConfig CreateDefault()
        {
            var ports = SerialPort.GetPortNames().OrderBy(p => p.Length).ThenBy(p => p).ToArray();
            var cfg = new AppConfig();
            var route = RouteConfig.CreateNew("Thiết bị 1");
            ((SerialEndpointConfig)route.Source).PortName = ports.FirstOrDefault() ?? "";
            route.Processing.Offset = 13;
            route.Outputs.Add(new WindowOutputConfig());
            cfg.Routes.Add(route);
            return cfg;
        }
    }

    // ---------------------------------------------------------------------
    public class UiConfig
    {
        [Category("Cửa sổ"), DisplayName("Luôn nằm trên cùng"), Description("Cửa sổ app luôn nổi trên các cửa sổ khác.")]
        public bool AlwaysOnTop { get; set; }

        [Category("Cửa sổ"), DisplayName("Thu nhỏ xuống khay"), Description("Khi thu nhỏ thì ẩn vào khay hệ thống (góc phải thanh taskbar) thay vì taskbar.")]
        public bool MinimizeToTray { get; set; } = true;

        [Category("Cửa sổ"), DisplayName("Mở lên ở dạng thu nhỏ"), Description("Khởi động là chạy ngầm luôn, không hiện cửa sổ.")]
        public bool StartMinimized { get; set; }

        [Category("Cửa sổ"), DisplayName("Khởi động cùng Windows"), Description("Tự chạy app khi đăng nhập Windows.")]
        public bool StartWithWindows { get; set; }

        [Category("Cửa sổ"), DisplayName("Hiện bảng Monitor"), Description("Hiện bảng theo dõi dữ liệu thô RX/TX bên dưới.")]
        public bool ShowMonitor { get; set; }

        [Category("Monitor"), DisplayName("Số dòng tối đa"), Description("Số dòng lưu trong bảng Monitor.")]
        public int MonitorMaxLines { get; set; } = 2000;

        [Browsable(false)] public int WindowX { get; set; } = int.MinValue;
        [Browsable(false)] public int WindowY { get; set; } = int.MinValue;
        [Browsable(false)] public int MonitorHeight { get; set; } = 260;

        public override string ToString() => "Giao diện";
    }

    // ---------------------------------------------------------------------
    //  Một "tuyến" = 1 nguồn dữ liệu → xử lý → N đầu ra
    // ---------------------------------------------------------------------
    public class RouteConfig
    {
        [Browsable(false)] public string Id { get; set; }

        [Category("1. Chung"), DisplayName("Tên tuyến"), Description("Tên hiển thị trên màn hình chính, ví dụ: Cân bàn 1.")]
        public string Name { get; set; } = "Thiết bị";

        [Category("1. Chung"), DisplayName("Bật"), Description("Tắt để tạm dừng tuyến này mà không phải xoá cấu hình.")]
        public bool Enabled { get; set; } = true;

        [Category("1. Chung"), DisplayName("Hiện ở màn hình chính"), Description("Ẩn đi nếu tuyến chỉ chạy ngầm (ví dụ chỉ chuyển tiếp COM→COM).")]
        public bool ShowOnMain { get; set; } = true;

        [Category("2. Đọc theo yêu cầu"), DisplayName("Lệnh khi bấm Đọc"),
         Description("Chuỗi gửi tới thiết bị khi bấm nút Đọc. Hỗ trợ \\r \\n \\t \\xHH. Để trống thì nút Đọc thành nút \"Gửi lại\" giá trị gần nhất.")]
        public string RequestCommand { get; set; } = "GET\\n";

        [Category("2. Đọc theo yêu cầu"), DisplayName("Tự gửi lệnh mỗi (ms)"),
         Description("Tự động gửi lệnh đọc theo chu kỳ (polling). 0 = tắt.")]
        public int PollIntervalMs { get; set; }

        [Browsable(false)]
        [XmlElement("Serial", typeof(SerialEndpointConfig))]
        [XmlElement("TcpClient", typeof(TcpClientEndpointConfig))]
        [XmlElement("TcpServer", typeof(TcpServerEndpointConfig))]
        public EndpointConfig Source { get; set; }

        [Browsable(false)]
        public ProcessingConfig Processing { get; set; }

        [Browsable(false)]
        [XmlArray("Outputs")]
        [XmlArrayItem("Window", typeof(WindowOutputConfig))]
        [XmlArrayItem("Keyboard", typeof(KeyboardOutputConfig))]
        [XmlArrayItem("SerialForward", typeof(SerialForwardOutputConfig))]
        [XmlArrayItem("TcpServerForward", typeof(TcpServerForwardOutputConfig))]
        [XmlArrayItem("TcpClientForward", typeof(TcpClientForwardOutputConfig))]
        [XmlArrayItem("Clipboard", typeof(ClipboardOutputConfig))]
        [XmlArrayItem("File", typeof(FileOutputConfig))]
        [XmlArrayItem("Http", typeof(HttpOutputConfig))]
        public List<OutputConfig> Outputs { get; set; }

        public static RouteConfig CreateNew(string name) => new RouteConfig
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = name,
            Source = new SerialEndpointConfig(),
            Processing = new ProcessingConfig(),
            Outputs = new List<OutputConfig>()
        };

        public override string ToString() => Name;
    }

    // =====================================================================
    //  NGUỒN / ĐÍCH KẾT NỐI (COM, TCP)
    // =====================================================================
    public enum LinkKind { Serial, TcpClient, TcpServer }

    [TypeConverter(typeof(ExpandableObjectConverter))]
    public abstract class EndpointConfig
    {
        [Browsable(false)] public abstract LinkKind Kind { get; }

        [Category("Kết nối lại"), DisplayName("Tự kết nối lại"), Description("Mất kết nối (rút cáp, thiết bị tắt) thì tự thử kết nối lại.")]
        public bool AutoReconnect { get; set; } = true;

        [Category("Kết nối lại"), DisplayName("Chu kỳ thử lại (ms)")]
        public int ReconnectIntervalMs { get; set; } = 2000;

        public abstract string Describe();
        public override string ToString() => Describe();
    }

    public class SerialEndpointConfig : EndpointConfig
    {
        public override LinkKind Kind => LinkKind.Serial;

        [Category("Cổng COM"), DisplayName("Cổng"), Description("Tên cổng, ví dụ COM3. Có thể gõ tay nếu cổng chưa cắm.")]
        [TypeConverter(typeof(ComPortConverter))]
        public string PortName { get; set; } = "";

        [Category("Cổng COM"), DisplayName("Baud rate")]
        [TypeConverter(typeof(BaudRateConverter))]
        public int BaudRate { get; set; } = 9600;

        [Category("Cổng COM"), DisplayName("Data bits")]
        public int DataBits { get; set; } = 8;

        [Category("Cổng COM"), DisplayName("Parity")]
        public Parity Parity { get; set; } = Parity.None;

        [Category("Cổng COM"), DisplayName("Stop bits")]
        public StopBits StopBits { get; set; } = StopBits.One;

        [Category("Cổng COM"), DisplayName("Handshake (bắt tay)")]
        public Handshake Handshake { get; set; } = Handshake.None;

        [Category("Cổng COM"), DisplayName("Bật DTR"), Description("Một số thiết bị cần DTR = bật mới gửi dữ liệu.")]
        public bool DtrEnable { get; set; }

        [Category("Cổng COM"), DisplayName("Bật RTS")]
        public bool RtsEnable { get; set; }

        public override string Describe()
        {
            string p = Parity == Parity.None ? "N" : Parity.ToString().Substring(0, 1);
            string s = StopBits == StopBits.One ? "1" : StopBits == StopBits.Two ? "2" : StopBits == StopBits.OnePointFive ? "1.5" : "0";
            return (string.IsNullOrEmpty(PortName) ? "(chưa chọn cổng)" : PortName) + $" · {BaudRate} {DataBits}{p}{s}";
        }
    }

    public class TcpClientEndpointConfig : EndpointConfig
    {
        public override LinkKind Kind => LinkKind.TcpClient;

        [Category("TCP"), DisplayName("Địa chỉ máy"), Description("IP hoặc tên máy cần kết nối tới (ví dụ bộ chuyển RS232→LAN).")]
        public string Host { get; set; } = "127.0.0.1";

        [Category("TCP"), DisplayName("Cổng TCP")]
        public int Port { get; set; } = 4001;

        public override string Describe() => $"TCP → {Host}:{Port}";
    }

    public class TcpServerEndpointConfig : EndpointConfig
    {
        public override LinkKind Kind => LinkKind.TcpServer;

        [Category("TCP"), DisplayName("Nghe trên địa chỉ"), Description("127.0.0.1 = chỉ app trên máy này kết nối được. 0.0.0.0 = cho cả máy khác trong mạng.")]
        public string ListenAddress { get; set; } = "127.0.0.1";

        [Category("TCP"), DisplayName("Cổng TCP")]
        public int Port { get; set; } = 4001;

        public override string Describe() => $"TCP server :{Port}";
    }

    // =====================================================================
    //  XỬ LÝ DỮ LIỆU
    // =====================================================================
    [TypeConverter(typeof(LocalizedEnumConverter))]
    public enum FrameMode
    {
        [Description("Theo dòng (CR / LF / CRLF)")] Line,
        [Description("Theo ký tự kết thúc tự chọn")] Delimiter,
        [Description("Độ dài cố định")] FixedLength,
        [Description("Theo khoảng lặng (ms)")] Idle,
        [Description("Không tách — mỗi gói nhận được là 1 khung")] Chunk
    }

    [TypeConverter(typeof(LocalizedEnumConverter))]
    public enum ExtractMode
    {
        [Description("Lấy cả khung")] WholeFrame,
        [Description("Biểu thức Regex")] Regex,
        [Description("Số đầu tiên trong khung")] FirstNumber,
        [Description("Số cuối cùng trong khung")] LastNumber
    }

    [TypeConverter(typeof(LocalizedEnumConverter))]
    public enum TextEncodingKind
    {
        [Description("ASCII")] Ascii,
        [Description("Latin-1 (ISO-8859-1)")] Latin1,
        [Description("UTF-8")] Utf8,
        [Description("Windows-1252")] Windows1252
    }

    public class ProcessingConfig
    {
        [Category("1. Tách khung"), DisplayName("Cách tách khung"),
         Description("Dữ liệu từ thiết bị đến từng mảnh; cần ghép lại thành từng \"khung\" (1 lần cân, 1 mã vạch...).")]
        public FrameMode FrameMode { get; set; } = FrameMode.Line;

        [Category("1. Tách khung"), DisplayName("Ký tự kết thúc"), Description("Dùng khi chọn \"Theo ký tự kết thúc\". Hỗ trợ \\r \\n \\t \\xHH, ví dụ \\x03 (ETX).")]
        public string Delimiter { get; set; } = "\\r\\n";

        [Category("1. Tách khung"), DisplayName("Độ dài khung (byte)"), Description("Dùng khi chọn \"Độ dài cố định\".")]
        public int FixedLength { get; set; } = 16;

        [Category("1. Tách khung"), DisplayName("Khoảng lặng kết thúc khung (ms)"),
         Description("Hết X ms không có dữ liệu mới thì coi như hết khung. Dùng cho chế độ \"Theo khoảng lặng\", hoặc làm chốt an toàn cho các chế độ khác (0 = tắt).")]
        public int IdleTimeoutMs { get; set; } = 100;

        [Category("1. Tách khung"), DisplayName("Bảng mã")]
        public TextEncodingKind Encoding { get; set; } = TextEncodingKind.Latin1;

        [Category("1. Tách khung"), DisplayName("Cắt khoảng trắng / ký tự điều khiển"), Description("Bỏ khoảng trắng, STX, ETX, NUL ở đầu và cuối khung.")]
        public bool Trim { get; set; } = true;

        [Category("2. Lấy giá trị"), DisplayName("Cách lấy giá trị")]
        public ExtractMode ExtractMode { get; set; } = ExtractMode.Regex;

        [Category("2. Lấy giá trị"), DisplayName("Regex"),
         Description("Biểu thức chính quy. Nếu có nhóm tên (?<value>...) hoặc nhóm số 1 thì lấy nhóm đó, không thì lấy cả phần khớp. Mặc định: số đứng trước đơn vị (12.5 kg → 12.5).")]
        public string Regex { get; set; } = @"[-+]?\d*\.?\d+(?=\s*[a-zA-Z])";

        [Category("2. Lấy giá trị"), DisplayName("Regex: không phân biệt hoa thường")]
        public bool RegexIgnoreCase { get; set; } = true;

        [Category("3. Xử lý số"), DisplayName("Là số"), Description("Bật thì giá trị được hiểu là số để nhân / cộng / làm tròn. Tắt thì giữ nguyên chuỗi.")]
        public bool IsNumber { get; set; } = true;

        [Category("3. Xử lý số"), DisplayName("Nhân với"), Description("Giá trị ra = giá trị đọc × Nhân với + Cộng thêm.")]
        public double Multiply { get; set; } = 1;

        [Category("3. Xử lý số"), DisplayName("Cộng thêm"), Description("Giá trị ra = giá trị đọc × Nhân với + Cộng thêm. Bản cũ cộng 13.")]
        public double Offset { get; set; }

        [Category("3. Xử lý số"), DisplayName("Số chữ số thập phân"), Description("-1 = giữ nguyên.")]
        public int Decimals { get; set; } = -1;

        [Category("3. Xử lý số"), DisplayName("Dùng dấu phẩy thập phân"), Description("Xuất 12,5 thay vì 12.5.")]
        public bool DecimalComma { get; set; }

        [Category("3. Xử lý số"), DisplayName("Bỏ qua nếu nhỏ hơn"), Description("Để trống = không giới hạn.")]
        public double? MinValue { get; set; }

        [Category("3. Xử lý số"), DisplayName("Bỏ qua nếu lớn hơn"), Description("Để trống = không giới hạn.")]
        public double? MaxValue { get; set; }

        [Category("4. Lọc"), DisplayName("Chờ ổn định (số lần lặp)"),
         Description("Chỉ gửi khi cùng 1 giá trị lặp lại liên tiếp N lần (cân gửi liên tục: chờ cân đứng yên), và gửi đúng 1 lần cho mỗi lần ổn định. 0 hoặc 1 = tắt.")]
        public int StableCount { get; set; }

        [Category("4. Lọc"), DisplayName("Bỏ qua giá trị trùng"), Description("Không gửi lại nếu giá trị giống hệt lần gửi trước.")]
        public bool SkipDuplicates { get; set; }

        [Category("4. Lọc"), DisplayName("Thời gian tính trùng (ms)"), Description("Giá trị trùng chỉ bị bỏ qua nếu cách lần trước ít hơn khoảng này. 0 = luôn bỏ qua.")]
        public int DuplicateWindowMs { get; set; }

        public override string ToString() => "Xử lý dữ liệu";
    }

    // =====================================================================
    //  ĐẦU RA
    // =====================================================================
    public abstract class OutputConfig
    {
        [Browsable(false)] public abstract string TypeTitle { get; }

        [Category("1. Chung"), DisplayName("Bật")]
        public bool Enabled { get; set; } = true;

        [Category("1. Chung"), DisplayName("Mẫu dữ liệu gửi"),
         Description("Nội dung gửi đi. Biến: {value} giá trị, {value:0.00} định dạng số, {frame} khung gốc, {hex}, {time} hoặc {time:yyyy-MM-dd HH:mm:ss}, {route}, {n} số thứ tự. Hỗ trợ \\r \\n \\t \\xHH.")]
        public string Template { get; set; } = "{value}";

        [Category("1. Chung"), DisplayName("Chờ trước khi gửi (ms)")]
        public int DelayMs { get; set; }

        public virtual string Describe() => TypeTitle;
        public override string ToString() => Describe();
    }

    [TypeConverter(typeof(LocalizedEnumConverter))]
    public enum FillMethod
    {
        [Description("UI Automation (khuyên dùng)")] UIAutomation,
        [Description("WM_SETTEXT (gán text trực tiếp)")] SetText,
        [Description("Gõ phím (giả lập bàn phím)")] TypeKeys
    }

    public class WindowOutputConfig : OutputConfig
    {
        public override string TypeTitle => "Điền vào ô của app khác";

        [Category("2. App đích"), DisplayName("Tên tiến trình"), Description("Tên file .exe không có đuôi, ví dụ WorkSupportSystem. Bấm \"Chọn ô bằng chuột\" để lấy tự động.")]
        [TypeConverter(typeof(ProcessNameConverter))]
        public string ProcessName { get; set; } = "WorkSupportSystem";

        [Category("2. App đích"), DisplayName("Tiêu đề cửa sổ chứa"), Description("Lọc thêm theo tiêu đề cửa sổ (không bắt buộc).")]
        public string WindowTitleContains { get; set; } = "";

        [Category("3. Ô nhập"), DisplayName("AutomationId"), Description("Định danh ô nhập (với app WinForms thường là tên control, ví dụ txtPO). Ổn định nhất; nếu có sẽ ưu tiên dùng.")]
        public string AutomationId { get; set; } = "";

        [Category("3. Ô nhập"), DisplayName("Class chứa"), Description("Một phần tên class của ô nhập. Để trống = gõ vào cửa sổ đang focus của app đó.")]
        public string ControlClassContains { get; set; } = "EDIT.app";

        [Category("3. Ô nhập"), DisplayName("Thứ tự ô (từ 0)"), Description("Ô thứ mấy trong các ô có class khớp.")]
        public int ControlIndex { get; set; }

        [Category("4. Cách điền"), DisplayName("Cách điền")]
        public FillMethod Method { get; set; } = FillMethod.UIAutomation;

        [Category("4. Cách điền"), DisplayName("Đưa cửa sổ lên trước")]
        public bool BringToFront { get; set; } = true;

        [Category("4. Cách điền"), DisplayName("Nhấn Enter sau khi điền")]
        public bool PressEnter { get; set; } = true;

        [Category("4. Cách điền"), DisplayName("Trả lại focus cho cửa sổ cũ"), Description("Điền xong thì quay lại cửa sổ người dùng đang làm việc.")]
        public bool RestoreFocus { get; set; }

        public override string Describe() => "Điền vào " + (string.IsNullOrEmpty(ProcessName) ? "app" : ProcessName);
    }

    public class KeyboardOutputConfig : OutputConfig
    {
        public override string TypeTitle => "Gõ phím vào cửa sổ đang mở";

        [Category("2. Gõ phím"), DisplayName("Nhấn Enter sau cùng")]
        public bool PressEnter { get; set; } = true;

        [Category("2. Gõ phím"), DisplayName("Nghỉ giữa các phím (ms)"), Description("Tăng lên nếu app đích bị mất ký tự.")]
        public int KeyDelayMs { get; set; }

        public override string Describe() => "Gõ phím (như máy quét mã vạch)";
    }

    [TypeConverter(typeof(LocalizedEnumConverter))]
    public enum ForwardMode
    {
        [Description("Dữ liệu thô (y nguyên từng byte)")] Raw,
        [Description("Giá trị đã xử lý (theo Mẫu dữ liệu)")] Value
    }

    public abstract class ForwardOutputConfig : OutputConfig
    {
        [Category("2. Chuyển tiếp"), DisplayName("Gửi gì"),
         Description("Thô: chép nguyên luồng byte như dây nối thẳng (dùng để chia cổng cho app khác qua com0com). Đã xử lý: chỉ gửi giá trị theo Mẫu dữ liệu.")]
        public ForwardMode Mode { get; set; } = ForwardMode.Raw;

        [Category("2. Chuyển tiếp"), DisplayName("Hai chiều"),
         Description("Dữ liệu app bên kia gửi về cũng được chuyển ngược về thiết bị nguồn (app bên kia điều khiển được thiết bị).")]
        public bool Bidirectional { get; set; } = true;

        [Browsable(false)] [XmlIgnore] public abstract EndpointConfig Target { get; }

        public override string Describe() => "Chuyển tiếp → " + Target.Describe() + (Mode == ForwardMode.Raw ? " (thô)" : "");
    }

    public class SerialForwardOutputConfig : ForwardOutputConfig
    {
        public override string TypeTitle => "Chuyển tiếp sang cổng COM";

        [Category("3. Cổng đích"), DisplayName("Cổng COM đích"), Description("Ví dụ 1 đầu của cặp com0com; app kia mở đầu còn lại.")]
        public SerialEndpointConfig Port { get; set; } = new SerialEndpointConfig();

        public override EndpointConfig Target => Port;
    }

    public class TcpServerForwardOutputConfig : ForwardOutputConfig
    {
        public override string TypeTitle => "Phát qua TCP server";

        [Category("3. TCP"), DisplayName("TCP server"), Description("App khác kết nối TCP vào đây để nhận dữ liệu (nhiều app cùng lúc được).")]
        public TcpServerEndpointConfig Server { get; set; } = new TcpServerEndpointConfig();

        public override EndpointConfig Target => Server;
    }

    public class TcpClientForwardOutputConfig : ForwardOutputConfig
    {
        public override string TypeTitle => "Gửi tới TCP server khác";

        [Category("3. TCP"), DisplayName("TCP đích")]
        public TcpClientEndpointConfig Client { get; set; } = new TcpClientEndpointConfig();

        public override EndpointConfig Target => Client;
    }

    public class ClipboardOutputConfig : OutputConfig
    {
        public override string TypeTitle => "Chép vào Clipboard";
        public override string Describe() => "Chép vào Clipboard";
    }

    public class FileOutputConfig : OutputConfig
    {
        public override string TypeTitle => "Ghi ra file (CSV / log)";

        public FileOutputConfig()
        {
            Template = "{time:yyyy-MM-dd HH:mm:ss},{value}";
        }

        [Category("2. File"), DisplayName("Đường dẫn"),
         Description("Có thể dùng biến {time:yyyyMMdd} để mỗi ngày 1 file, và biến môi trường như %USERPROFILE%.")]
        public string Path { get; set; } = @"%USERPROFILE%\Documents\ReadRS232\{route}_{time:yyyyMMdd}.csv";

        [Category("2. File"), DisplayName("Tự xuống dòng")]
        public bool AppendNewLine { get; set; } = true;

        [Category("2. File"), DisplayName("Dòng tiêu đề (file mới)"), Description("Ghi vào đầu file khi file mới được tạo, ví dụ: Thoi gian,Gia tri")]
        public string Header { get; set; } = "";

        public override string Describe() => "Ghi file " + System.IO.Path.GetFileName(Path);
    }

    [TypeConverter(typeof(LocalizedEnumConverter))]
    public enum HttpVerb { GET, POST, PUT }

    public class HttpOutputConfig : OutputConfig
    {
        public override string TypeTitle => "Gửi HTTP (API / webhook)";

        public HttpOutputConfig()
        {
            Template = "{\"route\":\"{route}\",\"value\":\"{value}\",\"time\":\"{time:o}\"}";
        }

        [Category("2. HTTP"), DisplayName("URL"), Description("Có thể chứa biến, ví dụ http://server/api/scale?v={value} (tự mã hoá URL).")]
        public string Url { get; set; } = "http://localhost:8080/api/value";

        [Category("2. HTTP"), DisplayName("Phương thức")]
        public HttpVerb Method { get; set; } = HttpVerb.POST;

        [Category("2. HTTP"), DisplayName("Content-Type")]
        public string ContentType { get; set; } = "application/json";

        [Category("2. HTTP"), DisplayName("Thời gian chờ (ms)")]
        public int TimeoutMs { get; set; } = 5000;

        public override string Describe() => "HTTP " + Method + " " + Url;
    }

    public static class OutputCatalog
    {
        public static readonly (string Title, Func<OutputConfig> Create)[] Types =
        {
            ("Điền vào ô của app khác", () => new WindowOutputConfig()),
            ("Gõ phím vào cửa sổ đang mở", () => new KeyboardOutputConfig()),
            ("Chuyển tiếp sang cổng COM (COM → COM)", () => new SerialForwardOutputConfig()),
            ("Phát qua TCP server (app khác kết nối vào)", () => new TcpServerForwardOutputConfig()),
            ("Gửi tới TCP server khác", () => new TcpClientForwardOutputConfig()),
            ("Chép vào Clipboard", () => new ClipboardOutputConfig()),
            ("Ghi ra file (CSV / log)", () => new FileOutputConfig()),
            ("Gửi HTTP (API / webhook)", () => new HttpOutputConfig()),
        };

        public static readonly (string Title, Func<EndpointConfig> Create)[] Sources =
        {
            ("Cổng COM (RS232 / USB-Serial)", () => new SerialEndpointConfig()),
            ("TCP client (kết nối tới bộ chuyển RS232→LAN...)", () => new TcpClientEndpointConfig()),
            ("TCP server (thiết bị / app khác kết nối vào)", () => new TcpServerEndpointConfig()),
        };
    }
}
