using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace CaptionTranslator
{
    /// <summary>
    /// Windows 11 look (rounded corners, dark frame, border colour) and correct maximizing for the borderless window.
    /// The DWM attributes are ignored on older Windows.
    /// </summary>
    public static class WindowEffects
    {
        private const int useImmersiveDarkMode = 20;
        private const int windowCornerPreference = 33;
        private const int borderColor = 34;
        private const int cornerRound = 2;
        private const int getMinMaxInfoMessage = 0x0024;
        private const int monitorDefaultToNearest = 2;

        public static void Apply(Window window, uint borderColorRgb)
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            int dark = 1;
            int corner = cornerRound;
            int colorRef = (int)(((borderColorRgb & 0xFF) << 16) | (borderColorRgb & 0xFF00) | ((borderColorRgb >> 16) & 0xFF));

            // Return codes are ignored on purpose: the attributes do not exist on Windows 10, and the app looks fine without them.
            _ = DwmSetWindowAttribute(handle, useImmersiveDarkMode, ref dark, sizeof(int));
            _ = DwmSetWindowAttribute(handle, windowCornerPreference, ref corner, sizeof(int));
            _ = DwmSetWindowAttribute(handle, borderColor, ref colorRef, sizeof(int));

            HwndSource.FromHwnd(handle)?.AddHook(WindowProc);
        }

        /// <summary>A borderless window maximizes over the taskbar; limit it to the monitor's work area instead.</summary>
        private static IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message != getMinMaxInfoMessage)
                return IntPtr.Zero;

            IntPtr monitor = MonitorFromWindow(hwnd, monitorDefaultToNearest);
            MonitorInfo info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (monitor == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
                return IntPtr.Zero;

            MinMaxInfo minMax = Marshal.PtrToStructure<MinMaxInfo>(lParam);
            minMax.MaxPosition.X = info.Work.Left - info.Monitor.Left;
            minMax.MaxPosition.Y = info.Work.Top - info.Monitor.Top;
            minMax.MaxSize.X = info.Work.Right - info.Work.Left;
            minMax.MaxSize.Y = info.Work.Bottom - info.Work.Top;
            Marshal.StructureToPtr(minMax, lParam, true);
            return IntPtr.Zero;
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MinMaxInfo
        {
            public NativePoint Reserved;
            public NativePoint MaxSize;
            public NativePoint MaxPosition;
            public NativePoint MinTrackSize;
            public NativePoint MaxTrackSize;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MonitorInfo
        {
            public int Size;
            public NativeRect Monitor;
            public NativeRect Work;
            public int Flags;
        }
    }
}
