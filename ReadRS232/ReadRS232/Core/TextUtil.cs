using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ReadRS232.Core
{
    /// <summary>Một giá trị đã xử lý xong, sẵn sàng gửi tới các đầu ra.</summary>
    public sealed class ValueEvent
    {
        public string Route;
        public string Value;
        public double? Number;
        public string Frame;
        public byte[] FrameBytes;
        public int ValueStart = -1;   // vị trí phần giá trị trong Frame
        public int ValueLength;
        public DateTime Time;
        public long Sequence;
    }

    public static class TextUtil
    {
        /// <summary>Chuyển chuỗi có \r \n \t \0 \e \\ \xHH thành ký tự thật.</summary>
        public static string Unescape(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('\\') < 0) return s ?? "";
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (c != '\\' || i == s.Length - 1) { sb.Append(c); continue; }
                char n = s[++i];
                switch (n)
                {
                    case 'r': sb.Append('\r'); break;
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case '0': sb.Append('\0'); break;
                    case 'e': sb.Append('\x1b'); break;
                    case '\\': sb.Append('\\'); break;
                    case 'x':
                        if (i + 2 < s.Length &&
                            int.TryParse(s.Substring(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int v))
                        {
                            sb.Append((char)v);
                            i += 2;
                        }
                        else sb.Append("\\x");
                        break;
                    default: sb.Append('\\').Append(n); break;
                }
            }
            return sb.ToString();
        }

        /// <summary>Hiển thị ký tự điều khiển dạng dễ đọc: \r \n &lt;STX&gt; ...</summary>
        public static string Visualize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.Length + 8);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '\r': sb.Append("\\r"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '\x02': sb.Append("<STX>"); break;
                    case '\x03': sb.Append("<ETX>"); break;
                    case '\x06': sb.Append("<ACK>"); break;
                    case '\x15': sb.Append("<NAK>"); break;
                    default:
                        if (c < 0x20 || c == 0x7f) sb.Append("\\x").Append(((int)c).ToString("X2"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        public static string ToHex(byte[] data, int max = 4096)
        {
            if (data == null || data.Length == 0) return "";
            int n = Math.Min(data.Length, max);
            var sb = new StringBuilder(n * 3);
            for (int i = 0; i < n; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(data[i].ToString("X2"));
            }
            if (n < data.Length) sb.Append(" …");
            return sb.ToString();
        }

        public static Encoding GetEncoding(TextEncodingKind kind)
        {
            switch (kind)
            {
                case TextEncodingKind.Ascii: return Encoding.ASCII;
                case TextEncodingKind.Utf8: return new UTF8Encoding(false);
                case TextEncodingKind.Windows1252: return Encoding.GetEncoding(1252);
                default: return Encoding.GetEncoding(28591);
            }
        }
    }

    /// <summary>
    /// Mẫu dữ liệu: "{value}", "{time:HH:mm:ss};{value:0.00}\r\n"...
    /// Chỉ thay các biến đã biết, nên JSON có dấu { } vẫn dùng bình thường.
    /// </summary>
    public sealed class OutputTemplate
    {
        static readonly Regex Token = new Regex(@"\{(value|same|frame|hex|time|route|n)(?::([^{}]*))?\}",
            RegexOptions.IgnoreCase | RegexOptions.Compiled | RegexOptions.CultureInvariant);

        readonly string _template;

        /// <param name="unescape">false cho đường dẫn file / URL, để "D:\new\..." không bị hiểu thành xuống dòng.</param>
        public OutputTemplate(string template, bool unescape = true)
        {
            template = string.IsNullOrEmpty(template) ? "{value}" : template;
            _template = unescape ? TextUtil.Unescape(template) : template;
        }

        public string Render(ValueEvent ev, Func<string, string> encode = null)
        {
            return Token.Replace(_template, m =>
            {
                string fmt = m.Groups[2].Success ? m.Groups[2].Value : null;
                string v;
                switch (m.Groups[1].Value.ToLowerInvariant())
                {
                    case "value":
                        v = fmt != null && ev.Number.HasValue
                            ? ev.Number.Value.ToString(fmt, CultureInfo.InvariantCulture)
                            : ev.Value;
                        break;
                    case "same": v = SameFrame(ev); break;
                    case "frame": v = ev.Frame; break;
                    case "hex": v = TextUtil.ToHex(ev.FrameBytes); break;
                    case "time": v = ev.Time.ToString(string.IsNullOrEmpty(fmt) ? "HH:mm:ss" : fmt, CultureInfo.InvariantCulture); break;
                    case "route": v = ev.Route; break;
                    case "n": v = ev.Sequence.ToString(fmt ?? "0", CultureInfo.InvariantCulture); break;
                    default: v = m.Value; break;
                }
                v = v ?? "";
                return encode != null ? encode(v) : v;
            });
        }

        /// <summary>{same}: khung gốc của thiết bị, chỉ thay phần số bằng giá trị đã xử lý, giữ đúng kiểu viết của số gốc.</summary>
        static string SameFrame(ValueEvent ev)
        {
            string f = ev.Frame ?? "";
            if (ev.ValueStart < 0 || ev.ValueLength <= 0 || ev.ValueStart + ev.ValueLength > f.Length) return f;
            string orig = f.Substring(ev.ValueStart, ev.ValueLength);
            string repl = ev.Number.HasValue ? FormatLike(orig, ev.Number.Value) ?? ev.Value : ev.Value;
            return f.Substring(0, ev.ValueStart) + repl + f.Substring(ev.ValueStart + ev.ValueLength);
        }

        /// <summary>
        /// Viết số mới theo đúng kiểu của số gốc: cùng số chữ số thập phân, cùng dấu thập phân (. hoặc ,),
        /// cùng độ rộng nếu gốc đệm số 0 (+00012.345), có dấu + nếu gốc có dấu. Trả null nếu gốc không phải số đơn giản.
        /// </summary>
        public static string FormatLike(string orig, double num)
        {
            string s = (orig ?? "").Trim();
            if (s.Length == 0) return null;
            char sign = s[0] == '+' || s[0] == '-' ? s[0] : '\0';
            string gap = "";
            if (sign != '\0')
            {
                s = s.Substring(1);
                if (s.StartsWith(" ")) { gap = " "; s = s.TrimStart(' '); }
            }
            int sep = s.LastIndexOfAny(new[] { '.', ',' });
            string ip = sep >= 0 ? s.Substring(0, sep) : s;
            string fp = sep >= 0 ? s.Substring(sep + 1) : "";
            if (ip.Length == 0 && fp.Length == 0) return null;
            foreach (char c in ip + fp) if (c < '0' || c > '9') return null;

            double abs = Math.Round(Math.Abs(num), fp.Length, MidpointRounding.AwayFromZero);
            string t = abs.ToString("F" + fp.Length, CultureInfo.InvariantCulture);
            int dot = t.IndexOf('.');
            string ni = dot >= 0 ? t.Substring(0, dot) : t;
            string nf = dot >= 0 ? t.Substring(dot + 1) : "";
            bool zeroPad = ip.Length > 1 && ip[0] == '0';
            if (zeroPad) ni = ni.PadLeft(ip.Length, '0');

            string ns = num < 0 && abs != 0 ? "-" : sign == '+' || (sign == '-' && zeroPad) ? "+" : "";
            return ns + (ns.Length > 0 ? gap : "") + ni + (sep >= 0 ? s[sep] + nf : "");
        }
    }
}
