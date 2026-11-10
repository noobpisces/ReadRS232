using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows.Automation;

namespace ReadRS232.Core
{
    /// <summary>Tìm cửa sổ / ô nhập của app khác và điền giá trị vào.</summary>
    public static class WindowTarget
    {
        public static IntPtr FindMainWindow(string processName, string titleContains)
        {
            processName = (processName ?? "").Trim();
            if (processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                processName = processName.Substring(0, processName.Length - 4);
            titleContains = (titleContains ?? "").Trim();

            var pids = new System.Collections.Generic.HashSet<uint>();
            if (processName.Length > 0)
            {
                foreach (var p in Process.GetProcessesByName(processName))
                {
                    pids.Add((uint)p.Id);
                    p.Dispose();
                }
                if (pids.Count == 0) return IntPtr.Zero;
            }
            else if (titleContains.Length == 0) return IntPtr.Zero;

            IntPtr best = IntPtr.Zero;
            Native.EnumWindows((h, _) =>
            {
                if (!Native.IsWindowVisible(h)) return true;
                if (pids.Count > 0)
                {
                    Native.GetWindowThreadProcessId(h, out uint pid);
                    if (!pids.Contains(pid)) return true;
                }
                string title = Native.TextOf(h);
                if (titleContains.Length > 0 && title.IndexOf(titleContains, StringComparison.OrdinalIgnoreCase) < 0) return true;
                if (title.Length == 0 && titleContains.Length == 0 && best != IntPtr.Zero) return true;
                best = h;
                return title.Length == 0; // cửa sổ có tiêu đề là ứng viên tốt nhất → dừng
            }, IntPtr.Zero);
            return best;
        }

        /// <summary>Tìm handle ô nhập theo class + thứ tự (giống cách bản cũ làm).</summary>
        public static IntPtr FindControlHandle(IntPtr root, string classContains, int index)
        {
            if (string.IsNullOrEmpty(classContains)) return IntPtr.Zero;
            var hits = Native.ChildWindows(root).Where(h => Native.ClassOf(h).IndexOf(classContains, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            return index >= 0 && index < hits.Count ? hits[index] : IntPtr.Zero;
        }

        public static string Fill(WindowOutputConfig c, string text)
        {
            IntPtr win = FindMainWindow(c.ProcessName, c.WindowTitleContains);
            if (win == IntPtr.Zero)
                throw new InvalidOperationException("Không thấy cửa sổ " + (string.IsNullOrEmpty(c.ProcessName) ? c.WindowTitleContains : c.ProcessName) + " (app chưa mở?)");

            IntPtr previous = Native.GetForegroundWindow();

            // ---- tìm ô nhập ----
            AutomationElement el = null;
            IntPtr ctl = IntPtr.Zero;
            if (!string.IsNullOrWhiteSpace(c.AutomationId))
            {
                try
                {
                    el = AutomationElement.FromHandle(win)?.FindFirst(TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.AutomationIdProperty, c.AutomationId.Trim()));
                    if (el != null)
                    {
                        object h = el.GetCurrentPropertyValue(AutomationElement.NativeWindowHandleProperty);
                        if (h is int hi && hi != 0) ctl = new IntPtr(hi);
                    }
                }
                catch (ElementNotAvailableException) { el = null; }
            }
            if (el == null && ctl == IntPtr.Zero && !string.IsNullOrEmpty(c.ControlClassContains))
            {
                ctl = FindControlHandle(win, c.ControlClassContains, c.ControlIndex);
                if (ctl == IntPtr.Zero)
                    throw new InvalidOperationException($"Không thấy ô nhập (class chứa \"{c.ControlClassContains}\", thứ tự {c.ControlIndex})");
            }
            if (el == null && ctl != IntPtr.Zero)
            {
                try { el = AutomationElement.FromHandle(ctl); } catch { }
            }

            bool needFocus = c.BringToFront || c.Method == FillMethod.TypeKeys || c.PressEnter;
            if (needFocus) Native.ForceForeground(win);
            // Chỉ gõ phím khi chắc chắn app đích đang ở trước — tránh gõ nhầm vào cửa sổ khác
            bool focused = SameProcess(Native.GetForegroundWindow(), win);

            if (el != null)
            {
                try
                {
                    if (!el.Current.IsEnabled) throw new InvalidOperationException("Ô nhập đang bị khoá (disabled)");
                }
                catch (ElementNotAvailableException) { }
            }

            // ---- điền ----
            switch (c.Method)
            {
                case FillMethod.UIAutomation when el != null && el.TryGetCurrentPattern(ValuePattern.Pattern, out object vp):
                    ((ValuePattern)vp).SetValue(text);
                    break;
                case FillMethod.TypeKeys:
                    if (!focused) throw new InvalidOperationException("Windows không cho đưa " + c.ProcessName + " lên trước nên không gõ phím được");
                    FocusElement(el);
                    Native.PressCombo(Native.VK_CONTROL, Native.VK_A);
                    Native.PressKey(Native.VK_BACK);
                    Native.TypeText(text, 0);
                    break;
                default:
                    // SetText, hoặc UIAutomation không dùng được → gán text trực tiếp
                    if (ctl != IntPtr.Zero) Native.SendMessage(ctl, Native.WM_SETTEXT, IntPtr.Zero, text);
                    else
                    {
                        if (!focused) throw new InvalidOperationException("Windows không cho đưa " + c.ProcessName + " lên trước nên không gõ phím được");
                        FocusElement(el);
                        Native.TypeText(text, 0);
                    }
                    break;
            }

            if (c.PressEnter)
            {
                FocusElement(el);
                Thread.Sleep(30);
                if (focused) Native.PressKey(Native.VK_RETURN);
                else if (ctl != IntPtr.Zero)
                {
                    // app đích không ở trước: gửi Enter thẳng vào ô nhập
                    Native.PostMessage(ctl, Native.WM_KEYDOWN, (IntPtr)Native.VK_RETURN, (IntPtr)0x001C0001);
                    Native.PostMessage(ctl, Native.WM_CHAR, (IntPtr)13, (IntPtr)0x001C0001);
                    Native.PostMessage(ctl, Native.WM_KEYUP, (IntPtr)Native.VK_RETURN, unchecked((IntPtr)(int)0xC01C0001));
                }
                else throw new InvalidOperationException("Đã điền nhưng không nhấn Enter được (app đích không ở trước)");
            }

            if (c.RestoreFocus && previous != IntPtr.Zero && previous != win)
            {
                Thread.Sleep(50);
                Native.ForceForeground(previous);
            }
            return "→ " + c.ProcessName;
        }

        static bool SameProcess(IntPtr a, IntPtr b)
        {
            if (a == IntPtr.Zero || b == IntPtr.Zero) return false;
            Native.GetWindowThreadProcessId(a, out uint pa);
            Native.GetWindowThreadProcessId(b, out uint pb);
            return pa == pb;
        }

        static void FocusElement(AutomationElement el)
        {
            if (el == null) return;
            try { el.SetFocus(); } catch { }
        }

        // ---------------- chọn ô bằng chuột ----------------
        public sealed class PickResult
        {
            public string ProcessName, WindowTitle, ClassName, ClassPart, AutomationId;
            public int Index;
            public IntPtr Control, Root;
            public override string ToString() =>
                $"{ProcessName} › {ClassPart} #{Index}" + (string.IsNullOrEmpty(AutomationId) ? "" : $" (Id: {AutomationId})");
        }

        public static PickResult Inspect(IntPtr ctl)
        {
            if (ctl == IntPtr.Zero) return null;
            var root = Native.GetAncestor(ctl, Native.GA_ROOT);
            var r = new PickResult
            {
                Control = ctl,
                Root = root,
                ProcessName = Native.ProcessNameOf(ctl),
                WindowTitle = Native.TextOf(root),
                ClassName = Native.ClassOf(ctl)
            };
            r.ClassPart = StableClassPart(r.ClassName);
            r.Index = root == ctl ? 0 : Native.ChildWindows(root)
                .Where(h => Native.ClassOf(h).IndexOf(r.ClassPart, StringComparison.OrdinalIgnoreCase) >= 0)
                .ToList().IndexOf(ctl);
            try
            {
                string id = AutomationElement.FromHandle(ctl).Current.AutomationId;
                // Với control Win32 thuần, AutomationId là số ID có thể trùng; vẫn hữu ích nhưng ưu tiên tên chữ
                if (!string.IsNullOrEmpty(id) && !id.All(char.IsDigit)) r.AutomationId = id;
            }
            catch { }
            return r;
        }

        /// <summary>"WindowsForms10.EDIT.app.0.34f5582_r8_ad1" → "EDIT.app" (phần đuôi đổi theo mỗi bản build của app kia).</summary>
        public static string StableClassPart(string cls)
        {
            if (string.IsNullOrEmpty(cls)) return "";
            if (cls.StartsWith("WindowsForms10.", StringComparison.Ordinal))
            {
                var parts = cls.Split('.');
                if (parts.Length >= 3) return parts[1] + "." + parts[2];
            }
            if (cls.StartsWith("HwndWrapper[", StringComparison.Ordinal)) return "HwndWrapper";
            return cls;
        }
    }
}
