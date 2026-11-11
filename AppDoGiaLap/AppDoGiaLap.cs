// App đo giả lập: mở 1 cổng COM, nghe dữ liệu như app đo thật, hiện giá trị + dữ liệu thô + trạng thái chân tín hiệu.
// Dùng để thử ReadRS232 / com0com khi không có app đo. Build: xem build.cmd.
//   AppDoGiaLap.exe [COMx]   → có tham số thì tự kết nối cổng đó khi mở.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new SimForm(args.Length > 0 ? args[0] : null));
    }
}

sealed class SimForm : Form
{
    static readonly Encoding Latin1 = Encoding.GetEncoding(28591);
    static readonly Regex NumberRx = new Regex(@"[-+]?\s?\d+(?:[.,]\d+)?", RegexOptions.Compiled);

    readonly ComboBox _port = Combo(90, true), _baud = Combo(75, true), _fmt = Combo(55, false),
                      _hs = Combo(90, false), _term = Combo(250, false);
    readonly CheckBox _dtr = new CheckBox { Text = "DTR", AutoSize = true, Checked = true, Margin = new Padding(8, 6, 0, 0) };
    readonly CheckBox _rts = new CheckBox { Text = "RTS", AutoSize = true, Checked = true, Margin = new Padding(4, 6, 0, 0) };
    readonly Button _conn = new Button { Text = "Kết nối", Width = 90, Height = 26 };
    readonly Label _value = new Label { Dock = DockStyle.Top, Height = 96, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Consolas", 40, FontStyle.Bold), Text = "---", BackColor = Color.FromArgb(20, 24, 32), ForeColor = Color.FromArgb(120, 220, 140) };
    readonly Label _status = new Label { Dock = DockStyle.Top, Height = 24, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(6, 0, 0, 0), Text = "Chưa kết nối" };
    readonly Label _pending = new Label { Dock = DockStyle.Top, Height = 24, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(6, 0, 0, 0), ForeColor = Color.FromArgb(200, 110, 0) };
    readonly TextBox _log = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font("Consolas", 10), BackColor = Color.White };
    readonly TextBox _send = new TextBox { Width = 260, Text = "1\\r" };
    readonly Timer _idle = new Timer { Interval = 150 };
    readonly Timer _pinTimer = new Timer { Interval = 300 };
    readonly List<byte> _buf = new List<byte>();
    SerialPort _sp;
    int _lines, _bytes;

    public SimForm(string autoPort)
    {
        Text = "App đo giả lập (thử ReadRS232)";
        ClientSize = new Size(900, 600);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9);

        _baud.Items.AddRange(new object[] { "1200", "2400", "4800", "9600", "19200", "38400", "57600", "115200" });
        _baud.Text = "9600";
        _fmt.Items.AddRange(new object[] { "8N1", "7E1", "7E2", "7O1", "8E1", "8O1", "8N2" });
        _fmt.SelectedIndex = 0;
        _hs.Items.AddRange(new object[] { "Không", "RTS/CTS", "XON/XOFF" });
        _hs.SelectedIndex = 0;
        _term.Items.AddRange(new object[]
        {
            "CR hoặc LF (dễ tính nhất)",
            "Chỉ CR",
            "Chỉ LF",
            "CR LF (phải đủ cả hai)",
            "Không cần, theo khoảng lặng 150 ms"
        });
        _term.SelectedIndex = 0;
        _port.DropDown += (s, e) => FillPorts();
        FillPorts();
        _port.Text = autoPort ?? (_port.Items.Contains("COM101") ? "COM101" : _port.Items.Count > 0 ? (string)_port.Items[_port.Items.Count - 1] : "");

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(4), WrapContents = true };
        top.Controls.AddRange(new Control[]
        {
            Cap("Cổng"), _port, Cap("Baud"), _baud, Cap("Khung"), _fmt, Cap("Handshake"), _hs, _dtr, _rts, _conn,
            Cap("Kết thúc dòng"), _term
        });
        top.SetFlowBreak(_conn, true);

        var clear = new Button { Text = "Xoá", Width = 60, Height = 26 };
        var sendBtn = new Button { Text = "Gửi", Width = 60, Height = 26 };
        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(4) };
        bottom.Controls.AddRange(new Control[] { Cap("Gửi về thiết bị (hỗ trợ \\r \\n \\xHH)"), _send, sendBtn, clear });

        Controls.Add(_log);
        Controls.Add(_pending);
        Controls.Add(_status);
        Controls.Add(_value);
        Controls.Add(top);
        Controls.Add(bottom);

        _conn.Click += (s, e) => { if (_sp == null) Connect(); else Disconnect(); };
        sendBtn.Click += (s, e) => Send();
        _send.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Send(); } };
        clear.Click += (s, e) => { _log.Clear(); _value.Text = "---"; _lines = _bytes = 0; _buf.Clear(); ShowPending(); };
        _term.SelectedIndexChanged += (s, e) => { _buf.Clear(); ShowPending(); };
        _dtr.CheckedChanged += (s, e) => { try { if (_sp != null) _sp.DtrEnable = _dtr.Checked; } catch { } };
        _rts.CheckedChanged += (s, e) => { try { if (_sp != null && _sp.Handshake == Handshake.None) _sp.RtsEnable = _rts.Checked; } catch { } };
        _idle.Tick += (s, e) => { _idle.Stop(); if (_term.SelectedIndex == 4 && _buf.Count > 0) { var f = _buf.ToArray(); _buf.Clear(); OnLine(f); ShowPending(); } };
        _pinTimer.Tick += (s, e) => ShowStatus();
        FormClosing += (s, e) => Disconnect();
        if (autoPort != null) Shown += (s, e) => Connect();
    }

    static ComboBox Combo(int w, bool editable) =>
        new ComboBox { Width = w, DropDownStyle = editable ? ComboBoxStyle.DropDown : ComboBoxStyle.DropDownList };

    static Label Cap(string t) => new Label { Text = t, AutoSize = true, Margin = new Padding(8, 7, 0, 0) };

    void FillPorts()
    {
        string keep = _port.Text;
        _port.Items.Clear();
        foreach (var p in SerialPort.GetPortNames().Distinct().OrderBy(p => p.Length).ThenBy(p => p)) _port.Items.Add(p);
        _port.Text = keep;
    }

    void Connect()
    {
        try
        {
            string f = (string)_fmt.SelectedItem;
            var parity = f[1] == 'E' ? Parity.Even : f[1] == 'O' ? Parity.Odd : Parity.None;
            var sp = new SerialPort(_port.Text.Trim(), int.Parse(_baud.Text.Trim()), parity, f[0] - '0', f[2] == '2' ? StopBits.Two : StopBits.One)
            {
                Handshake = _hs.SelectedIndex == 1 ? Handshake.RequestToSend : _hs.SelectedIndex == 2 ? Handshake.XOnXOff : Handshake.None,
                DtrEnable = _dtr.Checked,
                Encoding = Latin1,
                WriteTimeout = 1500
            };
            if (sp.Handshake == Handshake.None) sp.RtsEnable = _rts.Checked;
            sp.Open();
            sp.DataReceived += (s, e) =>
            {
                try
                {
                    int n = sp.BytesToRead;
                    if (n <= 0) return;
                    var b = new byte[n];
                    n = sp.Read(b, 0, n);
                    if (n < b.Length) Array.Resize(ref b, n);
                    BeginInvoke((Action)(() => OnBytes(b)));
                }
                catch { }
            };
            _sp = sp;
            _conn.Text = "Ngắt";
            _port.Enabled = _baud.Enabled = _fmt.Enabled = _hs.Enabled = false;
            Log("·", "Đã mở " + sp.PortName + " " + _baud.Text + " " + f);
            _pinTimer.Start();
            ShowStatus();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Không mở được " + _port.Text + ":\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    void Disconnect()
    {
        var sp = _sp;
        if (sp == null) return;
        _sp = null;
        _pinTimer.Stop();
        try { sp.Close(); } catch { }
        try { sp.Dispose(); } catch { }
        _conn.Text = "Kết nối";
        _port.Enabled = _baud.Enabled = _fmt.Enabled = _hs.Enabled = true;
        _status.Text = "Chưa kết nối";
        Log("·", "Đã đóng cổng");
    }

    void ShowStatus()
    {
        var sp = _sp;
        if (sp == null) return;
        try
        {
            _status.Text = string.Format("Đang nghe {0}   |   chân từ bên gửi: DSR {1}  CTS {2}  CD {3}   |   đã nhận {4} byte, {5} dòng",
                sp.PortName, OnOff(sp.DsrHolding), OnOff(sp.CtsHolding), OnOff(sp.CDHolding), _bytes, _lines);
        }
        catch (Exception ex) { _status.Text = "Lỗi cổng: " + ex.Message; }
    }

    static string OnOff(bool b) => b ? "BẬT" : "tắt";

    // ---------------- nhận ----------------
    void OnBytes(byte[] data)
    {
        _bytes += data.Length;
        Log("RX ", Visualize(Latin1.GetString(data)).PadRight(28) + "  | " + string.Join(" ", data.Select(x => x.ToString("X2"))));
        _buf.AddRange(data);
        int mode = _term.SelectedIndex;
        if (mode == 4) { _idle.Stop(); _idle.Start(); }
        else
        {
            int start = 0;
            for (int i = 0; i < _buf.Count; i++)
            {
                byte b = _buf[i];
                bool hit = mode == 0 ? b == 13 || b == 10
                         : mode == 1 ? b == 13
                         : mode == 2 ? b == 10
                         : b == 10 && i > start && _buf[i - 1] == 13;
                if (!hit) continue;
                int end = mode == 3 ? i - 1 : i;
                if (end > start) OnLine(_buf.GetRange(start, end - start).ToArray());
                start = i + 1;
            }
            if (start > 0) _buf.RemoveRange(0, start);
        }
        ShowPending();
        ShowStatus();
    }

    void OnLine(byte[] line)
    {
        string text = Latin1.GetString(line).Trim('\r', '\n', ' ', '\0');
        if (text.Length == 0) return;
        _lines++;
        var ms = NumberRx.Matches(text);
        if (ms.Count == 0)
        {
            _value.Text = "?";
            Log("==>", "DÒNG \"" + Visualize(text) + "\"  (không tìm thấy số)");
            return;
        }
        string raw = ms[ms.Count - 1].Value.Replace(" ", "").Replace(',', '.');
        double v;
        int dot = raw.IndexOf('.');
        string shown = double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out v)
            ? v.ToString("F" + (dot < 0 ? 0 : raw.Length - dot - 1), CultureInfo.InvariantCulture)   // bỏ dấu + và số 0 đệm
            : raw;
        _value.Text = shown;
        Log("==>", "DÒNG \"" + Visualize(text) + "\"  →  giá trị " + shown);
    }

    void ShowPending()
    {
        _pending.Text = _buf.Count == 0 ? ""
            : "Đã nhận nhưng CHƯA có ký tự kết thúc dòng, đang chờ: \"" + Visualize(Latin1.GetString(_buf.ToArray())) + "\"";
    }

    // ---------------- gửi ----------------
    void Send()
    {
        if (_sp == null) { Log("!", "Chưa kết nối"); return; }
        try
        {
            var bytes = Latin1.GetBytes(Unescape(_send.Text));
            _sp.Write(bytes, 0, bytes.Length);
            Log("TX ", Visualize(Latin1.GetString(bytes)));
        }
        catch (Exception ex) { Log("!", "Gửi lỗi: " + ex.Message); }
    }

    // ---------------- tiện ích ----------------
    void Log(string tag, string text)
    {
        if (_log.TextLength > 300000) _log.Text = _log.Text.Substring(150000);
        _log.AppendText(DateTime.Now.ToString("HH:mm:ss.fff") + "  " + tag.PadRight(3) + "  " + text + "\r\n");
    }

    static string Visualize(string s)
    {
        var sb = new StringBuilder();
        foreach (char c in s)
        {
            if (c == '\r') sb.Append("\\r");
            else if (c == '\n') sb.Append("\\n");
            else if (c < 0x20 || c == 0x7f) sb.Append("\\x").Append(((int)c).ToString("X2"));
            else sb.Append(c);
        }
        return sb.ToString();
    }

    static string Unescape(string s)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c != '\\' || i == s.Length - 1) { sb.Append(c); continue; }
            char n = s[++i];
            int v;
            if (n == 'r') sb.Append('\r');
            else if (n == 'n') sb.Append('\n');
            else if (n == 't') sb.Append('\t');
            else if (n == '\\') sb.Append('\\');
            else if (n == 'x' && i + 2 < s.Length && int.TryParse(s.Substring(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v)) { sb.Append((char)v); i += 2; }
            else sb.Append('\\').Append(n);
        }
        return sb.ToString();
    }
}
