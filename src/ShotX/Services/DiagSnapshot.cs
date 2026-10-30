using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ShotX.Services;

/// <summary>
/// Renders a visual to a PNG when SHOTX_SNAPSHOT_DIR is set, so UI changes can be verified by
/// reading the resulting image instead of guessing blind. Mirrors ReminderX's DiagSnapshot.
/// </summary>
public static class DiagSnapshot
{
    public static bool Enabled => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SHOTX_SNAPSHOT_DIR"));

    public static void Capture(Visual visual, string name)
    {
        var dir = Environment.GetEnvironmentVariable("SHOTX_SNAPSHOT_DIR");
        if (string.IsNullOrWhiteSpace(dir)) return;

        try
        {
            Directory.CreateDirectory(dir);
            var bounds = VisualTreeHelper.GetDescendantBounds(visual);
            var width = Math.Max(1, (int)bounds.Width);
            var height = Math.Max(1, (int)bounds.Height);
            if (visual is FrameworkElement fe)
            {
                width = Math.Max(1, (int)fe.ActualWidth);
                height = Math.Max(1, (int)fe.ActualHeight);
            }

            var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(visual);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            var path = Path.Combine(dir, $"{name}.png");
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
            encoder.Save(stream);
            Logger.Log($"DiagSnapshot: wrote {path}");
        }
        catch (Exception ex)
        {
            Logger.Log($"DiagSnapshot.Capture failed for {name}: {ex.Message}");
        }
    }
}
