using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using FlowDirection = System.Windows.FlowDirection;

namespace ShotX.Services;

/// <summary>Builds a synthetic "screenshot" so the editor UI can be exercised and snapshotted
/// without needing a real screen capture (useful when running headless / in a sandbox).</summary>
public static class TestImageFactory
{
    public static BitmapSource CreateSampleScreenshot(int width = 1280, int height = 800)
    {
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(Color.FromRgb(0x2C, 0x3E, 0x66), Color.FromRgb(0x1B, 0x1F, 0x3B), 45), null,
                new Rect(0, 0, width, height));

            dc.DrawRoundedRectangle(Brushes.White, null, new Rect(60, 60, width - 120, 90), 14, 14);
            var title = new FormattedText("World Emblem - Panel de ejemplo", System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, new Typeface("Segoe UI Semibold"), 26, Brushes.Black, 1.25);
            dc.DrawText(title, new Point(84, 90));

            for (var i = 0; i < 4; i++)
            {
                var cardX = 60 + i * ((width - 120) / 4.0);
                var cardColor = new[] { Color.FromRgb(10, 132, 255), Color.FromRgb(52, 199, 89), Color.FromRgb(255, 149, 0), Color.FromRgb(175, 82, 222) }[i];
                dc.DrawRoundedRectangle(new SolidColorBrush(cardColor), null,
                    new Rect(cardX, 180, (width - 120) / 4.0 - 16, 140), 12, 12);
            }

            dc.DrawRoundedRectangle(Brushes.White, null, new Rect(60, 350, width - 120, height - 410), 14, 14);
            var bodyFont = new Typeface("Segoe UI");
            for (var row = 0; row < 8; row++)
            {
                var text = new FormattedText($"Fila de ejemplo {row + 1} - dato de prueba generado",
                    System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, bodyFont, 15,
                    Brushes.Black, 1.25);
                dc.DrawText(text, new Point(84, 376 + row * 32));
            }
        }

        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }
}
