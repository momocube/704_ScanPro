#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace ScanPro704
{
    /// <summary>
    /// Keeps Unity's secondary-display venue window visible above ordinary desktop windows
    /// without activating it or stealing focus from the operator dashboard.
    /// </summary>
    public static class NativeVenueDisplayWindow
    {
        const int GwlExStyle = -20;
        const long WsExTopmost = 0x00000008L;
        const uint MonitorDefaultToNearest = 2;
        const uint SwpNoSize = 0x0001;
        const uint SwpNoMove = 0x0002;
        const uint SwpNoActivate = 0x0010;
        const uint SwpShowWindow = 0x0040;
        const string SecondaryDisplayTitle = "Unity Secondary Display";

        static readonly IntPtr HwndTopmost = new IntPtr(-1);

        delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

        [DllImport("user32.dll")]
        static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetWindowText(IntPtr window, StringBuilder text, int maxCount);

        [DllImport("user32.dll")]
        static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("user32.dll")]
        static extern bool IsWindowVisible(IntPtr window);

        [DllImport("user32.dll")]
        static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", EntryPoint = "SetWindowPos")]
        static extern bool SetWindowPosNative(IntPtr window, IntPtr insertAfter,
            int x, int y, int width, int height, uint flags);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
        static extern IntPtr GetWindowLongPtr64(IntPtr window, int index);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        static extern int GetWindowLong32(IntPtr window, int index);

        public static bool TryKeepOnTopWithoutActivating(IntPtr primaryWindow, out string reason)
        {
            var outputWindow = FindSecondaryOutput(primaryWindow);
            if (outputWindow == IntPtr.Zero)
            {
                reason = "Visible Unity Secondary Display window was not found on another monitor.";
                return false;
            }

            var foregroundBefore = GetForegroundWindow();
            if (!SetWindowPosNative(outputWindow, HwndTopmost, 0, 0, 0, 0,
                    SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow))
            {
                reason = "SetWindowPos could not keep the secondary output topmost.";
                return false;
            }

            if (GetForegroundWindow() != foregroundBefore)
            {
                reason = "The secondary output visibility update changed the foreground window.";
                return false;
            }

            if (!HasTopmostStyle(outputWindow))
            {
                reason = "The secondary output window did not retain the topmost style.";
                return false;
            }

            reason = null;
            return true;
        }

        public static bool IsSecondaryOutputTopmost(IntPtr primaryWindow)
        {
            var outputWindow = FindSecondaryOutput(primaryWindow);
            return outputWindow != IntPtr.Zero && HasTopmostStyle(outputWindow);
        }

        static IntPtr FindSecondaryOutput(IntPtr primaryWindow)
        {
            if (primaryWindow == IntPtr.Zero) return IntPtr.Zero;
            var primaryMonitor = MonitorFromWindow(primaryWindow, MonitorDefaultToNearest);
            if (primaryMonitor == IntPtr.Zero) return IntPtr.Zero;

            var processId = (uint)Process.GetCurrentProcess().Id;
            var found = IntPtr.Zero;
            EnumWindows((window, parameter) =>
            {
                if (!IsWindowVisible(window)) return true;
                GetWindowThreadProcessId(window, out var ownerProcessId);
                if (ownerProcessId != processId || MonitorFromWindow(window, MonitorDefaultToNearest) == primaryMonitor)
                    return true;

                var title = new StringBuilder(128);
                GetWindowText(window, title, title.Capacity);
                if (!string.Equals(title.ToString(), SecondaryDisplayTitle, StringComparison.Ordinal))
                    return true;

                found = window;
                return false;
            }, IntPtr.Zero);
            return found;
        }

        static bool HasTopmostStyle(IntPtr window)
        {
            var style = IntPtr.Size == 8
                ? GetWindowLongPtr64(window, GwlExStyle).ToInt64()
                : GetWindowLong32(window, GwlExStyle);
            return (style & WsExTopmost) != 0;
        }
    }
}
#endif
