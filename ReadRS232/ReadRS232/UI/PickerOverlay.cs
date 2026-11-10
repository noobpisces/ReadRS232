using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using ReadRS232.Core;

namespace ReadRS232.UI
{
    /// <summary>
    /// "Chọn ô bằng chuột": rê chuột lên ô nhập của app khác (viền đỏ bao quanh),
    /// bấm chuột trái để chọn, chuột phải / Esc để huỷ. Cú click bị chặn lại nên app kia không bị ảnh hưởng.
    /// </summary>
    public sealed class PickerOverlay : IDisposable
    {
        delegate IntPtr HookProc(int code, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int id, HookProc fn, IntPtr mod, uint thread);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr h);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(string name);
        const int WH_MOUSE_LL = 14, WH_KEYBOARD_LL = 13;
        const int WM_LBUTTONDOWN = 0x201, WM_LBUTTONUP = 0x202, WM_RBUTTONDOWN = 0x204, WM_RBUTTONUP = 0x205, WM_KEYDOWN = 0x100;

        readonly HookProc _mouseProc, _keyProc;   // giữ tham chiếu để GC không thu hồi
        IntPtr _mouseHook, _keyHook;
        readonly HighlightForm _box = new HighlightForm();
        readonly InfoForm _info = new InfoForm();
        readonly Timer _timer = new Timer { Interval = 60 };
        readonly Action<WindowTarget.PickResult> _done;
        IntPtr _current;
        bool _finished, _swallowUp;
        readonly int _myPid = Process.GetCurrentProcess().Id;

        public PickerOverlay(Action<WindowTarget.PickResult> done)
        {
            _done = done;
            _mouseProc = MouseHook;
            _keyProc = KeyHook;
        }

        public void Start()
        {
            IntPtr mod = GetModuleHandle(null);
            _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, mod, 0);
            _keyHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyProc, mod, 0);
            var started = DateTime.Now;
            _timer.Tick += (s, e) =>
            {
                if (_finished) return;
                if ((DateTime.Now - started).TotalSeconds > 30) { Finish(false); return; }   // tự huỷ sau 30s
                Track();
            };
            _timer.Start();
            _info.Show();
            Track();
        }

        void Track()
        {
            Native.GetCursorPos(out var pt);
            _info.Location = new Point(pt.X + 18, pt.Y + 22);
            IntPtr h = Native.WindowFromPoint(pt);
            Native.GetWindowThreadProcessId(h, out uint pid);
            if (pid == _myPid) return;   // đang trỏ vào chính khung viền / khung info
            if (h == _current) return;
            _current = h;
            Native.GetWindowRect(h, out var r);
            _box.Bounds = Rectangle.FromLTRB(r.Left - 3, r.Top - 3, r.Right + 3, r.Bottom + 3);
            if (!_box.Visible) _box.Show();
            var info = WindowTarget.Inspect(h);
            _info.SetText(info == null ? "" :
                $"{info.ProcessName}\n{info.ClassPart}  #{info.Index}" + (string.IsNullOrEmpty(info.AutomationId) ? "" : $"\nId: {info.AutomationId}") +
                "\n\nClick trái: chọn   ·   Chuột phải / Esc: huỷ");
        }

        IntPtr MouseHook(int code, IntPtr w, IntPtr l)
        {
            if (code >= 0 && !_finished)
            {
                int msg = w.ToInt32();
                if (msg == WM_LBUTTONDOWN) { _swallowUp = true; Finish(true); return (IntPtr)1; }
                if (msg == WM_RBUTTONDOWN) { _swallowUp = true; Finish(false); return (IntPtr)1; }
            }
            if (code >= 0 && _swallowUp && (w.ToInt32() == WM_LBUTTONUP || w.ToInt32() == WM_RBUTTONUP))
            {
                _swallowUp = false;
                return (IntPtr)1;
            }
            return CallNextHookEx(_mouseHook, code, w, l);
        }

        IntPtr KeyHook(int code, IntPtr w, IntPtr l)
        {
            if (code >= 0 && w.ToInt32() == WM_KEYDOWN && Marshal.ReadInt32(l) == (int)Keys.Escape && !_finished)
            {
                Finish(false);
                return (IntPtr)1;
            }
            return CallNextHookEx(_keyHook, code, w, l);
        }

        void Finish(bool ok)
        {
            _finished = true;
            Native.GetCursorPos(out var pt);
            IntPtr h = Native.WindowFromPoint(pt);
            Native.GetWindowThreadProcessId(h, out uint pid);
            if (pid == _myPid) h = _current;
            var result = ok ? WindowTarget.Inspect(h) : null;
            // gỡ hook sau khi nhả chuột để nuốt luôn sự kiện nhả
            var t = new Timer { Interval = 250 };
            t.Tick += (s, e) => { t.Dispose(); Dispose(); _done(result); };
            t.Start();
            _box.Hide();
            _info.Hide();
        }

        public void Dispose()
        {
            _timer.Dispose();
            if (_mouseHook != IntPtr.Zero) { UnhookWindowsHookEx(_mouseHook); _mouseHook = IntPtr.Zero; }
            if (_keyHook != IntPtr.Zero) { UnhookWindowsHookEx(_keyHook); _keyHook = IntPtr.Zero; }
            _box.Dispose();
            _info.Dispose();
        }

        // Khung viền đỏ trong suốt, không nhận chuột
        sealed class HighlightForm : Form
        {
            public HighlightForm()
            {
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                TopMost = true;
                StartPosition = FormStartPosition.Manual;
                BackColor = Color.Magenta;
                TransparencyKey = Color.Magenta;
            }
            protected override bool ShowWithoutActivation => true;
            protected override CreateParams CreateParams
            {
                get { var cp = base.CreateParams; cp.ExStyle |= 0x20 | 0x80 | 0x08000000; return cp; } // click xuyên qua, toolwindow, không lấy focus
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                using (var p = new Pen(Theme.Err, 3)) e.Graphics.DrawRectangle(p, 1, 1, Width - 3, Height - 3);
            }
            protected override void OnResize(EventArgs e) { base.OnResize(e); Invalidate(); }
        }

        sealed class InfoForm : Form
        {
            readonly Label _l;
            public InfoForm()
            {
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                TopMost = true;
                StartPosition = FormStartPosition.Manual;
                BackColor = Theme.Surface;
                AutoSize = true;
                AutoSizeMode = AutoSizeMode.GrowAndShrink;
                Opacity = 0.96;
                Padding = new Padding(Theme.S(10), Theme.S(8), Theme.S(10), Theme.S(8));
                _l = Theme.Label("Rê chuột lên ô cần điền…", Theme.UI, Theme.Text);
                Controls.Add(_l);
            }
            public void SetText(string s) => _l.Text = s;
            protected override bool ShowWithoutActivation => true;
            protected override CreateParams CreateParams
            {
                get { var cp = base.CreateParams; cp.ExStyle |= 0x20 | 0x80 | 0x08000000; return cp; }
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                using (var p = new Pen(Theme.Accent)) e.Graphics.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
            }
        }
    }
}
