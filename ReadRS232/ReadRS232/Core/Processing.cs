using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace ReadRS232.Core
{
    // =====================================================================
    //  Framer: ghép các mảnh byte thành từng khung hoàn chỉnh.
    // =====================================================================
    public sealed class Framer : IDisposable
    {
        const int MaxFrame = 16 * 1024;
        readonly FrameMode _mode;
        readonly byte[] _delim;
        readonly int _fixed;
        readonly int _idleMs;
        readonly List<byte> _buf = new List<byte>(256);
        readonly object _lk = new object();
        readonly Timer _timer;

        public event Action<byte[]> Frame;

        public Framer(ProcessingConfig c, Encoding enc)
        {
            _mode = c.FrameMode;
            _delim = enc.GetBytes(TextUtil.Unescape(c.Delimiter ?? ""));
            _fixed = Math.Max(1, c.FixedLength);
            _idleMs = Math.Max(0, c.IdleTimeoutMs);
            if (_mode == FrameMode.Idle && _idleMs == 0) _idleMs = 50;
            if (_mode == FrameMode.Delimiter && _delim.Length == 0) _mode = FrameMode.Line;
            _timer = new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
        }

        public void Push(byte[] data)
        {
            List<byte[]> frames = null;
            lock (_lk)
            {
                if (_mode == FrameMode.Chunk)
                {
                    (frames = new List<byte[]>()).Add(data);
                }
                else
                {
                    _buf.AddRange(data);
                    switch (_mode)
                    {
                        case FrameMode.Line: SplitLines(ref frames); break;
                        case FrameMode.Delimiter: SplitDelimiter(ref frames); break;
                        case FrameMode.FixedLength:
                            while (_buf.Count >= _fixed)
                            {
                                Add(ref frames, _buf.GetRange(0, _fixed).ToArray());
                                _buf.RemoveRange(0, _fixed);
                            }
                            break;
                    }
                    if (_buf.Count > MaxFrame) { Add(ref frames, _buf.ToArray()); _buf.Clear(); }
                    if (_idleMs > 0) _timer.Change(_buf.Count > 0 ? _idleMs : Timeout.Infinite, Timeout.Infinite);
                }
            }
            if (frames != null) foreach (var f in frames) Frame?.Invoke(f);
        }

        void SplitLines(ref List<byte[]> frames)
        {
            int start = 0;
            for (int i = 0; i < _buf.Count; i++)
            {
                byte b = _buf[i];
                if (b != (byte)'\r' && b != (byte)'\n') continue;
                if (i > start) Add(ref frames, _buf.GetRange(start, i - start).ToArray());
                start = i + 1;
            }
            if (start > 0) _buf.RemoveRange(0, start);
        }

        void SplitDelimiter(ref List<byte[]> frames)
        {
            int start = 0;
            for (int i = 0; i <= _buf.Count - _delim.Length; i++)
            {
                bool hit = true;
                for (int k = 0; k < _delim.Length; k++)
                    if (_buf[i + k] != _delim[k]) { hit = false; break; }
                if (!hit) continue;
                Add(ref frames, _buf.GetRange(start, i - start).ToArray());
                i += _delim.Length - 1;
                start = i + 1;
            }
            if (start > 0) _buf.RemoveRange(0, start);
        }

        static void Add(ref List<byte[]> frames, byte[] f)
        {
            if (frames == null) frames = new List<byte[]>();
            frames.Add(f);
        }

        public void Flush()
        {
            byte[] f = null;
            lock (_lk)
            {
                if (_buf.Count > 0) { f = _buf.ToArray(); _buf.Clear(); }
            }
            if (f != null) Frame?.Invoke(f);
        }

        public void Dispose() => _timer.Dispose();
    }

    // =====================================================================
    //  ValueProcessor: khung → giá trị (regex, số, nhân/cộng, lọc).
    // =====================================================================
    public sealed class ProcessResult
    {
        public bool Ok;
        public bool Quiet;        // bị bỏ qua nhưng không cần ghi log (khung rỗng, chờ ổn định)
        public string Reason;
        public string Text;
        public string Value;
        public double? Number;
    }

    public sealed class ValueProcessor
    {
        static readonly Regex NumberRx = new Regex(@"[-+]?\s?\d+(?:[.,]\d+)*", RegexOptions.Compiled);
        static readonly char[] TrimChars = { ' ', '\t', '\r', '\n', '\0', '\x02', '\x03', '\x06', '\x15' };

        readonly ProcessingConfig _c;
        readonly Regex _rx;
        public Encoding Encoding { get; }

        string _lastEmitted;
        DateTime _lastEmitTime;
        string _stableCandidate;
        int _stableHits;
        volatile bool _forceNext;

        public string RegexError { get; }

        public ValueProcessor(ProcessingConfig c)
        {
            _c = c;
            Encoding = TextUtil.GetEncoding(c.Encoding);
            if (c.ExtractMode == ExtractMode.Regex && !string.IsNullOrEmpty(c.Regex))
            {
                try
                {
                    var opt = RegexOptions.CultureInvariant | (c.RegexIgnoreCase ? RegexOptions.IgnoreCase : 0);
                    _rx = new Regex(c.Regex, opt, TimeSpan.FromMilliseconds(200));
                }
                catch (ArgumentException ex) { RegexError = ex.Message; }
            }
        }

        /// <summary>Kết quả tiếp theo luôn được gửi, bỏ qua lọc trùng / chờ ổn định (dùng khi bấm Đọc).</summary>
        public void ForceNext() => _forceNext = true;

        public ProcessResult Process(byte[] frame)
        {
            var r = new ProcessResult();
            string text = Encoding.GetString(frame);
            if (_c.Trim) text = text.Trim(TrimChars);
            r.Text = text;
            if (text.Length == 0) { r.Quiet = true; r.Reason = "khung rỗng"; return r; }

            // ---- lấy giá trị ----
            string value;
            switch (_c.ExtractMode)
            {
                case ExtractMode.Regex:
                    if (RegexError != null) { r.Reason = "Regex sai: " + RegexError; return r; }
                    if (_rx == null) { value = text; break; }
                    Match m;
                    try { m = _rx.Match(text); }
                    catch (RegexMatchTimeoutException) { r.Reason = "Regex chạy quá lâu"; return r; }
                    if (!m.Success) { r.Reason = "không khớp Regex"; return r; }
                    var g = m.Groups["value"];
                    value = g.Success ? g.Value : m.Groups.Count > 1 && m.Groups[1].Success ? m.Groups[1].Value : m.Value;
                    break;
                case ExtractMode.FirstNumber:
                case ExtractMode.LastNumber:
                    var ms = NumberRx.Matches(text);
                    if (ms.Count == 0) { r.Reason = "không có số"; return r; }
                    value = (_c.ExtractMode == ExtractMode.FirstNumber ? ms[0] : ms[ms.Count - 1]).Value;
                    break;
                default:
                    value = text;
                    break;
            }

            // ---- xử lý số ----
            if (_c.IsNumber)
            {
                if (!TryParseNumber(value, out double num)) { r.Reason = $"\"{value}\" không phải số"; return r; }
                num = num * _c.Multiply + _c.Offset;
                if (_c.Decimals >= 0) num = Math.Round(num, Math.Min(_c.Decimals, 15), MidpointRounding.AwayFromZero);
                if (_c.MinValue.HasValue && num < _c.MinValue.Value) { r.Reason = $"{num} nhỏ hơn giới hạn"; return r; }
                if (_c.MaxValue.HasValue && num > _c.MaxValue.Value) { r.Reason = $"{num} lớn hơn giới hạn"; return r; }
                value = _c.Decimals >= 0
                    ? num.ToString("F" + Math.Min(_c.Decimals, 15), CultureInfo.InvariantCulture)
                    : num.ToString("0.##########", CultureInfo.InvariantCulture);
                if (_c.DecimalComma) value = value.Replace('.', ',');
                r.Number = num;
            }
            r.Value = value;

            // ---- lọc ----
            bool force = _forceNext;
            if (_c.StableCount > 1)
            {
                if (value == _stableCandidate) _stableHits++;
                else { _stableCandidate = value; _stableHits = 1; }
                if (!force)
                {
                    if (_stableHits < _c.StableCount) { r.Quiet = true; r.Reason = $"chờ ổn định {_stableHits}/{_c.StableCount}"; return r; }
                    if (_stableHits > _c.StableCount) { r.Quiet = true; r.Reason = "đã gửi giá trị ổn định này"; return r; }
                }
            }
            if (_c.SkipDuplicates && !force && value == _lastEmitted &&
                (_c.DuplicateWindowMs <= 0 || (DateTime.Now - _lastEmitTime).TotalMilliseconds < _c.DuplicateWindowMs))
            {
                r.Reason = "trùng giá trị trước";
                return r;
            }

            _forceNext = false;
            _lastEmitted = value;
            _lastEmitTime = DateTime.Now;
            r.Ok = true;
            return r;
        }

        /// <summary>Hiểu được 12.5 / 12,5 / 1,234.5 / 1.234,5 / "+ 12.5" bất kể Windows đang dùng ngôn ngữ gì.</summary>
        public static bool TryParseNumber(string s, out double v)
        {
            v = 0;
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Replace(" ", "").Trim();
            int dot = s.LastIndexOf('.'), comma = s.LastIndexOf(',');
            if (dot >= 0 && comma >= 0)
            {
                if (comma > dot) s = s.Replace(".", "").Replace(',', '.');   // 1.234,5
                else s = s.Replace(",", "");                                // 1,234.5
            }
            else if (comma >= 0) s = s.Replace(',', '.');
            return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out v);
        }
    }
}
