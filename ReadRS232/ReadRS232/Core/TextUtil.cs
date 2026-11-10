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
        static readonly Regex Token = new Regex(@"\{(value|frame|hex|time|route|n)(?::([^{}]*))?\}",
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
    }
}
