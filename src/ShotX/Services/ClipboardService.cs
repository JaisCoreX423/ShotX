using System.Threading;
using System.Windows.Media.Imaging;

namespace ShotX.Services;

public static class ClipboardService
{
    public static void SetImage(BitmapSource image)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                System.Windows.Clipboard.SetImage(image);
                return;
            }
            catch (Exception ex)
            {
                // Another app can be holding the clipboard open momentarily; retry briefly.
                Logger.Log($"Clipboard.SetImage attempt {attempt + 1} failed: {ex.Message}");
                Thread.Sleep(80);
            }
        }
    }
}
