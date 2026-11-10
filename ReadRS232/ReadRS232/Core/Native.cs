using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace ReadRS232.Core
{
    /// <summary>Win32 API dùng cho việc tìm cửa sổ, điền dữ liệu và giả lập bàn phím.</summary>
    public static class Native
    {
        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
        [DllImport("user32.dll")] public static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc cb, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hWnd, StringBuilder sb, int max);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int cmd);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr hWnd);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
        [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool attach);
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);
        [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr w, string l);
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, INPUT[] inputs, int size);
        [DllImport("user32.dll")] public static extern bool PeekMessage(out MSG msg, IntPtr hWnd, uint min, uint max, uint remove);
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        public const uint WM_SETTEXT = 0x000C, WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101, WM_CHAR = 0x0102;
        public const uint GA_ROOT = 2;

        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
        [StructLayout(LayoutKind.Sequential)]
        public struct MSG { public IntPtr hwnd; public uint message; public IntPtr wParam, lParam; public uint time; public POINT pt; }

        // ---- SendInput ----
        [StructLayout(LayoutKind.Sequential)]
        struct INPUT { public uint type; public InputUnion u; }
        [StructLayout(LayoutKind.Explicit)]
        struct InputUnion
        {
            [FieldOffset(0)] public MOUSEINPUT mi;
            [FieldOffset(0)] public KEYBDINPUT ki;
        }
        [StructLayout(LayoutKind.Sequential)]
        struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr extra; }
        [StructLayout(LayoutKind.Sequential)]
        struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr extra; }

        const uint INPUT_KEYBOARD = 1, KEYEVENTF_KEYUP = 2, KEYEVENTF_UNICODE = 4;
        public const ushort VK_RETURN = 0x0D, VK_TAB = 0x09, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_BACK = 0x08, VK_A = 0x41;

        static INPUT Key(ushort vk, ushort scan, uint flags) =>
            new INPUT { type = INPUT_KEYBOARD, u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = flags } } };

        public static void PressKey(ushort vk)
        {
            var arr = new[] { Key(vk, 0, 0), Key(vk, 0, KEYEVENTF_KEYUP) };
            SendInput(2, arr, Marshal.SizeOf(typeof(INPUT)));
        }

        public static void PressCombo(ushort modifier, ushort vk)
        {
            var arr = new[] { Key(modifier, 0, 0), Key(vk, 0, 0), Key(vk, 0, KEYEVENTF_KEYUP), Key(modifier, 0, KEYEVENTF_KEYUP) };
            SendInput(4, arr, Marshal.SizeOf(typeof(INPUT)));
        }

        /// <summary>Gõ chuỗi bất kỳ (kể cả tiếng Việt) vào cửa sổ đang có focus. \r/\n = Enter, \t = Tab.</summary>
        public static void TypeText(string text, int delayMs)
        {
            int size = Marshal.SizeOf(typeof(INPUT));
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\r' || c == '\n')
                {
                    if (c == '\n' && i > 0 && text[i - 1] == '\r') continue;
                    PressKey(VK_RETURN);
                }
                else if (c == '\t') PressKey(VK_TAB);
                else
                {
                    var arr = new[] { Key(0, c, KEYEVENTF_UNICODE), Key(0, c, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP) };
                    SendInput(2, arr, size);
                }
                if (delayMs > 0) Thread.Sleep(delayMs);
            }
        }

        public static string ClassOf(IntPtr h) { var sb = new StringBuilder(256); GetClassName(h, sb, sb.Capacity); return sb.ToString(); }
        public static string TextOf(IntPtr h) { var sb = new StringBuilder(512); GetWindowText(h, sb, sb.Capacity); return sb.ToString(); }

        public static string ProcessNameOf(IntPtr h)
        {
            GetWindowThreadProcessId(h, out uint pid);
            try { using (var p = Process.GetProcessById((int)pid)) return p.ProcessName; }
            catch { return ""; }
        }

        public static List<IntPtr> ChildWindows(IntPtr parent)
        {
            var list = new List<IntPtr>();
            EnumChildWindows(parent, (h, _) => { list.Add(h); return true; }, IntPtr.Zero);
            return list;
        }

        /// <summary>Đưa 1 cửa sổ lên trước. Windows hay chặn việc này, nên thử lần lượt nhiều cách.</summary>
        public static bool ForceForeground(IntPtr hWnd)
        {
            if (IsIconic(hWnd)) ShowWindow(hWnd, 9 /*SW_RESTORE*/);
            if (GetForegroundWindow() == hWnd) return true;
            if (SetForegroundWindow(hWnd) && GetForegroundWindow() == hWnd) return true;

            IntPtr fg = GetForegroundWindow();
            uint fgThread = GetWindowThreadProcessId(fg, out _);
            uint me = GetCurrentThreadId();
            if (fgThread != 0 && fgThread != me)
            {
                AttachThreadInput(me, fgThread, true);
                BringWindowToTop(hWnd);
                SetForegroundWindow(hWnd);
                AttachThreadInput(me, fgThread, false);
            }
            if (GetForegroundWindow() == hWnd) return true;

            // Mẹo cuối: gửi 1 sự kiện chuột "dịch 0 pixel" để Windows coi app này vừa nhận input
            // và cho phép đổi foreground. (Không dùng mẹo nhấn ALT: ALT mở menu hệ thống của app đích
            // và phím Enter sau đó bị menu nuốt mất.)
            var nudge = new[] { new INPUT { type = 0 /*INPUT_MOUSE*/, u = new InputUnion { mi = new MOUSEINPUT { dwFlags = 0x0001 /*MOVE*/ } } } };
            SendInput(1, nudge, Marshal.SizeOf(typeof(INPUT)));
            SetForegroundWindow(hWnd);
            if (GetForegroundWindow() != hWnd)
            {
                ShowWindow(hWnd, 6 /*SW_MINIMIZE*/);
                ShowWindow(hWnd, 9 /*SW_RESTORE*/);   // khôi phục từ thu nhỏ luôn được Windows cho lên trước
                SetForegroundWindow(hWnd);
            }
            return GetForegroundWindow() == hWnd;
        }

        /// <summary>Tạo hàng đợi message cho thread hiện tại (cần cho AttachThreadInput/SetForeground từ thread nền).</summary>
        public static void EnsureMessageQueue() => PeekMessage(out _, IntPtr.Zero, 0, 0, 0);

        public static void UseDarkTitleBar(IntPtr hwnd)
        {
            try
            {
                int on = 1;
                if (DwmSetWindowAttribute(hwnd, 20, ref on, 4) != 0) DwmSetWindowAttribute(hwnd, 19, ref on, 4);
            }
            catch { }
        }
    }
}
