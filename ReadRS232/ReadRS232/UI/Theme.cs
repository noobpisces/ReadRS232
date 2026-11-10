using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.IO.Ports;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;

namespace ReadRS232.UI
{
    public static class Theme
    {
        public static readonly Color Bg = Color.FromArgb(0x15, 0x17, 0x1A);
        public static readonly Color Surface = Color.FromArgb(0x1E, 0x21, 0x26);
        public static readonly Color Surface2 = Color.FromArgb(0x27, 0x2B, 0x31);
        public static readonly Color Border = Color.FromArgb(0x34, 0x39, 0x41);
        public static readonly Color Text = Color.FromArgb(0xE8, 0xEA, 0xED);
        public static readonly Color Muted = Color.FromArgb(0x8E, 0x95, 0x9E);
        public static readonly Color Accent = Color.FromArgb(0x4C, 0xA8, 0xFF);
        public static readonly Color Ok = Color.FromArgb(0x3D, 0xD6, 0x8C);
        public static readonly Color Warn = Color.FromArgb(0xF5, 0xB0, 0x41);
        public static readonly Color Err = Color.FromArgb(0xFF, 0x6B, 0x6B);
        public static readonly Color Rx = Color.FromArgb(0x7F, 0xD1, 0xFF);
        public static readonly Color Tx = Color.FromArgb(0xFF, 0xC8, 0x7A);

        public static readonly Font UI = new Font("Segoe UI", 9.5f);
        public static readonly Font Bold = new Font("Segoe UI Semibold", 9.5f);
        public static readonly Font Small = new Font("Segoe UI", 8.5f);
        public static readonly Font Value = new Font("Segoe UI Semibold", 26f);
        public static readonly Font Mono = new Font("Consolas", 9.5f);
        public static readonly Font Icon = new Font("Segoe MDL2 Assets", 10.5f);

        static float _scale = -1;
        public static float Scale
        {
            get
            {
                if (_scale < 0)
                    using (var g = Graphics.FromHwnd(IntPtr.Zero)) _scale = g.DpiX / 96f;
                return _scale;
            }
        }
        public static int S(int px) => (int)Math.Round(px * Scale);

        public static Button Button(string text, bool primary = false)
        {
            var b = new Button
            {
                Text = text,
                FlatStyle = FlatStyle.Flat,
                BackColor = primary ? Accent : Surface2,
                ForeColor = primary ? Color.FromArgb(0x0B, 0x1A, 0x2B) : Text,
                Font = primary ? Bold : UI,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Padding = new Padding(S(10), S(3), S(10), S(3)),
                Cursor = Cursors.Hand,
                UseVisualStyleBackColor = false
            };
            b.FlatAppearance.BorderColor = primary ? Accent : Border;
            b.FlatAppearance.MouseOverBackColor = primary ? ControlPaint.Light(Accent, 0.2f) : Color.FromArgb(0x31, 0x36, 0x3E);
            b.FlatAppearance.MouseDownBackColor = primary ? ControlPaint.Dark(Accent, 0.05f) : Border;
            return b;
        }

        public static Button IconButton(string glyph, string tip, ToolTip tt)
        {
            var b = new Button
            {
                Text = glyph,
                Font = Icon,
                FlatStyle = FlatStyle.Flat,
                ForeColor = Muted,
                BackColor = Bg,
                Size = new Size(S(32), S(28)),
                Margin = new Padding(0),
                Cursor = Cursors.Hand,
                TabStop = false
            };
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = Surface2;
            b.FlatAppearance.MouseDownBackColor = Border;
            tt?.SetToolTip(b, tip);
            return b;
        }

        public static Label Label(string text, Font font = null, Color? color = null) => new Label
        {
            Text = text,
            AutoSize = true,
            Font = font ?? UI,
            ForeColor = color ?? Text,
            BackColor = Color.Transparent
        };

        public static void StyleCombo(ComboBox c)
        {
            c.FlatStyle = FlatStyle.Flat;
            c.BackColor = Surface2;
            c.ForeColor = Text;
            c.Font = UI;
        }

        public static void StyleText(TextBox t)
        {
            t.BorderStyle = BorderStyle.FixedSingle;
            t.BackColor = Surface2;
            t.ForeColor = Text;
            t.Font = UI;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr h, int msg, IntPtr w, string l);
        public static void Placeholder(TextBox t, string text) => t.HandleCreated += (s, e) => SendMessage(t.Handle, 0x1501, (IntPtr)1, text);

        public static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = radius * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void Apply(Form f)
        {
            f.BackColor = Bg;
            f.ForeColor = Text;
            f.Font = UI;
            f.HandleCreated += (s, e) => Core.Native.UseDarkTitleBar(f.Handle);
            try { f.Icon = System.Drawing.Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
        }

        public static ToolStripRenderer MenuRenderer => new ToolStripProfessionalRenderer(new DarkMenuColors()) { RoundedEdges = false };

        public static ContextMenuStrip Menu()
        {
            var m = new ContextMenuStrip { Renderer = MenuRenderer, Font = UI, ForeColor = Text, BackColor = Surface, ShowImageMargin = false };
            return m;
        }

        public static ToolStripMenuItem Item(string text, EventHandler click)
        {
            return new ToolStripMenuItem(text, null, click) { ForeColor = Text };
        }

        class DarkMenuColors : ProfessionalColorTable
        {
            public override Color ToolStripDropDownBackground => Surface;
            public override Color MenuItemSelected => Surface2;
            public override Color MenuItemBorder => Border;
            public override Color MenuBorder => Border;
            public override Color MenuItemSelectedGradientBegin => Surface2;
            public override Color MenuItemSelectedGradientEnd => Surface2;
            public override Color ImageMarginGradientBegin => Surface;
            public override Color ImageMarginGradientMiddle => Surface;
            public override Color ImageMarginGradientEnd => Surface;
            public override Color SeparatorDark => Border;
            public override Color SeparatorLight => Surface;
        }
    }

    // =====================================================================
    //  TypeConverter cho màn hình Cài đặt (PropertyGrid)
    // =====================================================================

    /// <summary>True/False → Có/Không trong màn hình Cài đặt.</summary>
    public class VietBoolConverter : BooleanConverter
    {
        public override object ConvertTo(ITypeDescriptorContext ctx, CultureInfo culture, object value, Type dest)
        {
            if (dest == typeof(string) && value is bool b) return b ? "Có" : "Không";
            return base.ConvertTo(ctx, culture, value, dest);
        }
        public override object ConvertFrom(ITypeDescriptorContext ctx, CultureInfo culture, object value)
        {
            if (value is string s) { if (s == "Có") return true; if (s == "Không") return false; }
            return base.ConvertFrom(ctx, culture, value);
        }
        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext c) => new StandardValuesCollection(new object[] { true, false });
    }

    /// <summary>Hiện enum bằng chữ trong [Description] (tiếng Việt).</summary>
    public class LocalizedEnumConverter : EnumConverter
    {
        public LocalizedEnumConverter(Type t) : base(t) { }

        static string Desc(Type t, object v)
        {
            var fi = t.GetField(Enum.GetName(t, v) ?? "");
            var a = fi?.GetCustomAttribute<DescriptionAttribute>();
            return a?.Description ?? v.ToString();
        }

        public override object ConvertTo(ITypeDescriptorContext ctx, CultureInfo culture, object value, Type dest)
        {
            if (dest == typeof(string) && value != null && value.GetType().IsEnum) return Desc(EnumType, value);
            return base.ConvertTo(ctx, culture, value, dest);
        }

        public override object ConvertFrom(ITypeDescriptorContext ctx, CultureInfo culture, object value)
        {
            if (value is string s)
                foreach (var v in Enum.GetValues(EnumType))
                    if (Desc(EnumType, v) == s) return v;
            return base.ConvertFrom(ctx, culture, value);
        }
    }

    public class ComPortConverter : StringConverter
    {
        public override bool GetStandardValuesSupported(ITypeDescriptorContext c) => true;
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext c) => false;
        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext c) =>
            new StandardValuesCollection(PortCatalog.Names());
    }

    public class BaudRateConverter : Int32Converter
    {
        static readonly int[] Rates = { 1200, 2400, 4800, 9600, 14400, 19200, 38400, 57600, 115200, 230400, 460800, 921600 };
        public override bool GetStandardValuesSupported(ITypeDescriptorContext c) => true;
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext c) => false;
        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext c) => new StandardValuesCollection(Rates);
    }

    public class ProcessNameConverter : StringConverter
    {
        public override bool GetStandardValuesSupported(ITypeDescriptorContext c) => true;
        public override bool GetStandardValuesExclusive(ITypeDescriptorContext c) => false;
        public override StandardValuesCollection GetStandardValues(ITypeDescriptorContext c)
        {
            var names = Process.GetProcesses()
                .Where(p => { try { return p.MainWindowHandle != IntPtr.Zero && p.Id != Process.GetCurrentProcess().Id; } catch { return false; } })
                .Select(p => p.ProcessName).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n).ToArray();
            return new StandardValuesCollection(names);
        }
    }

    /// <summary>Danh sách cổng COM kèm tên thiết bị (lấy từ WMI, có cache).</summary>
    public static class PortCatalog
    {
        static System.Collections.Generic.Dictionary<string, string> _friendly =
            new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public static string[] Names() =>
            SerialPort.GetPortNames().Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n.Length).ThenBy(n => n, StringComparer.OrdinalIgnoreCase).ToArray();

        public static string Friendly(string port)
        {
            lock (_friendly) return _friendly.TryGetValue(port ?? "", out var f) ? f : null;
        }

        /// <summary>Đọc tên thân thiện (chậm ~0.2s, nên gọi ở thread nền).</summary>
        public static void Refresh()
        {
            var map = new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                using (var s = new System.Management.ManagementObjectSearcher(
                    "SELECT Name FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'"))
                    foreach (var o in s.Get())
                    {
                        string name = o["Name"] as string;
                        if (name == null) continue;
                        var m = System.Text.RegularExpressions.Regex.Match(name, @"\((COM\d+)\)");
                        if (m.Success) map[m.Groups[1].Value] = name.Replace(m.Value, "").Trim();
                    }
            }
            catch { }
            try
            {
                // Cổng com0com không có trong PnP với tên COMx → đọc từ registry SERIALCOMM
                using (var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM"))
                    if (k != null)
                        foreach (var vn in k.GetValueNames())
                        {
                            string port = k.GetValue(vn) as string;
                            if (port == null || map.ContainsKey(port)) continue;
                            if (vn.IndexOf("com0com", StringComparison.OrdinalIgnoreCase) >= 0) map[port] = "com0com (cổng ảo)";
                        }
            }
            catch { }
            lock (_friendly) _friendly = map;
        }
    }
}
