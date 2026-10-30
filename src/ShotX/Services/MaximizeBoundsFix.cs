using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ShotX.Services;

/// <summary>
/// A borderless (WindowStyle=None + WindowChrome) window maximizes to the full monitor rectangle
/// by default, covering the taskbar and pushing content (like a bottom action bar) off-screen.
/// Hooking WM_GETMINMAXINFO caps it to the monitor's work area instead. Same fix ReminderX needed.
/// </summary>
public static class MaximizeBoundsFix
{
    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr handle, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, MONITORINFO lpmi);

    private const uint MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private class MONITORINFO
    {
        public int cbSize = Marshal.SizeOf(typeof(MONITORINFO));
        public RECT rcMonitor;
        public RECT rcWork;
        public int dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    private const int WM_GETMINMAXINFO = 0x0024;

    public static void Apply(Window window)
    {
        window.SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(window).Handle;
            HwndSource.FromHwnd(handle)?.AddHook(WndProc);
        };
    }

    private static IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_GETMINMAXINFO)
        {
            var mmi = Marshal.PtrToStructure<MINMAXINFO>(lParam);

            var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
            if (monitor != IntPtr.Zero)
            {
                var info = new MONITORINFO();
                if (GetMonitorInfo(monitor, info))
                {
                    var work = info.rcWork;
                    var bounds = info.rcMonitor;
                    mmi.ptMaxPosition.X = Math.Abs(work.Left - bounds.Left);
                    mmi.ptMaxPosition.Y = Math.Abs(work.Top - bounds.Top);
                    mmi.ptMaxSize.X = Math.Abs(work.Right - work.Left);
                    mmi.ptMaxSize.Y = Math.Abs(work.Bottom - work.Top);
                }
            }

            Marshal.StructureToPtr(mmi, lParam, true);
            handled = true;
        }
        return IntPtr.Zero;
    }
}
