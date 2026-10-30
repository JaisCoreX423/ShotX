using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using Rectangle = System.Drawing.Rectangle;

namespace ShotX.Services;

public static class ScreenCaptureService
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(POINT point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hWnd, uint gaFlags);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X, Y;
    }

    private const uint GA_ROOT = 2;

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int dwAttribute, out RECT pvAttribute, int cbAttribute);

    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    /// <summary>Virtual screen bounds spanning every monitor, in device pixels.</summary>
    public static Rectangle GetVirtualScreenBounds()
    {
        return new Rectangle(
            System.Windows.Forms.SystemInformation.VirtualScreen.X,
            System.Windows.Forms.SystemInformation.VirtualScreen.Y,
            System.Windows.Forms.SystemInformation.VirtualScreen.Width,
            System.Windows.Forms.SystemInformation.VirtualScreen.Height);
    }

    /// <summary>Captures the full virtual desktop (all monitors) as a GDI+ bitmap.</summary>
    public static Bitmap CaptureVirtualScreen()
    {
        var bounds = GetVirtualScreenBounds();
        var bmp = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
        using (var g = Graphics.FromImage(bmp))
        {
            g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size, CopyPixelOperation.SourceCopy);
        }
        return bmp;
    }

    /// <summary>Foreground window bounds (excluding the drop shadow), in virtual-screen device pixels.</summary>
    public static Rectangle? GetActiveWindowBounds()
    {
        var hWnd = GetForegroundWindow();
        if (hWnd == IntPtr.Zero) return null;

        var vs = GetVirtualScreenBounds();

        if (DwmGetWindowAttribute(hWnd, DWMWA_EXTENDED_FRAME_BOUNDS, out var rect, Marshal.SizeOf<RECT>()) == 0)
        {
            return ClampToVirtualScreen(new Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top), vs);
        }

        if (GetWindowRect(hWnd, out rect))
        {
            return ClampToVirtualScreen(new Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top), vs);
        }

        return null;
    }

    /// <summary>
    /// Finds the top-level window under a screen point (device pixels), skipping ShotX's own
    /// windows and the desktop/shell, for the capture overlay's window-snap suggestion.
    /// </summary>
    public static Rectangle? GetWindowBoundsAtPoint(System.Drawing.Point screenPointPx)
    {
        var hWnd = WindowFromPoint(new POINT { X = screenPointPx.X, Y = screenPointPx.Y });
        if (hWnd == IntPtr.Zero) return null;

        hWnd = GetAncestor(hWnd, GA_ROOT);
        if (hWnd == IntPtr.Zero) return null;

        GetWindowThreadProcessId(hWnd, out var pid);
        if (pid == (uint)Environment.ProcessId) return null;

        var className = new System.Text.StringBuilder(256);
        GetClassName(hWnd, className, className.Capacity);
        var cls = className.ToString();
        if (cls is "Progman" or "WorkerW" or "Shell_TrayWnd") return null;

        var vs = GetVirtualScreenBounds();
        if (DwmGetWindowAttribute(hWnd, DWMWA_EXTENDED_FRAME_BOUNDS, out var rect, Marshal.SizeOf<RECT>()) == 0)
        {
            var bounds = new Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
            return bounds.Width > 0 && bounds.Height > 0 ? ClampToVirtualScreen(bounds, vs) : null;
        }

        if (GetWindowRect(hWnd, out rect))
        {
            var bounds = new Rectangle(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top);
            return bounds.Width > 0 && bounds.Height > 0 ? ClampToVirtualScreen(bounds, vs) : null;
        }

        return null;
    }

    private static Rectangle ClampToVirtualScreen(Rectangle rect, Rectangle virtualScreen)
    {
        var intersected = Rectangle.Intersect(rect, virtualScreen);
        return intersected.IsEmpty ? rect : intersected;
    }

    public static Bitmap Crop(Bitmap source, Rectangle regionInVirtualScreenCoords)
    {
        var vs = GetVirtualScreenBounds();
        var local = new Rectangle(
            regionInVirtualScreenCoords.X - vs.X,
            regionInVirtualScreenCoords.Y - vs.Y,
            regionInVirtualScreenCoords.Width,
            regionInVirtualScreenCoords.Height);

        local.Intersect(new Rectangle(0, 0, source.Width, source.Height));
        if (local.Width <= 0 || local.Height <= 0)
        {
            local = new Rectangle(0, 0, Math.Max(1, source.Width), Math.Max(1, source.Height));
        }

        return source.Clone(local, source.PixelFormat);
    }

    public static BitmapSource ToBitmapSource(Bitmap bitmap)
    {
        var hBitmap = bitmap.GetHbitmap();
        try
        {
            var source = Imaging.CreateBitmapSourceFromHBitmap(
                hBitmap, IntPtr.Zero, Int32Rect.Empty,
                BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            return source;
        }
        finally
        {
            DeleteObject(hBitmap);
        }
    }

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr hObject);

    /// <summary>Test-only: converts a WPF BitmapSource back into a GDI+ Bitmap (reverse of
    /// <see cref="ToBitmapSource"/>), so synthetic test images can feed the capture overlay.</summary>
    public static System.Drawing.Bitmap FromBitmapSource(BitmapSource source)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(source));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        stream.Position = 0;
        return new System.Drawing.Bitmap(stream);
    }
}
