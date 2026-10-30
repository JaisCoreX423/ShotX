using System.Windows.Media.Imaging;
using ShotX.Views;

namespace ShotX.Services;

/// <summary>Orchestrates a capture end-to-end: grab pixels, copy to clipboard, open the editor.</summary>
public class CaptureController
{
    public BitmapSource? LastCapture { get; private set; }

    /// <summary>Hides the main launcher window if it's visible (so it never ends up in the
    /// screenshot) and reports whether it actually hid something. Set by App.xaml.cs.</summary>
    public Func<bool>? HideMainWindowIfVisible { get; set; }

    /// <summary>Lets App.xaml.cs hook up each freshly-created editor window (e.g. wiring its
    /// Settings button to the shared Settings window) without CaptureController needing to know
    /// anything about App itself.</summary>
    public Action<EditorWindow>? ConfigureNewEditorWindow { get; set; }

    public async void CaptureRegion()
    {
        try
        {
            await HideMainWindowAndWaitAsync();

            var full = ScreenCaptureService.CaptureVirtualScreen();
            var bounds = ScreenCaptureService.GetVirtualScreenBounds();
            var scale = GetPrimaryScale();

            var overlay = new CaptureOverlayWindow(full, bounds, scale);
            overlay.Show();
            var region = await overlay.SelectionTask;

            if (region is { } r && r.Width > 0 && r.Height > 0)
            {
                using var cropped = ScreenCaptureService.Crop(full, r);
                Finish(ScreenCaptureService.ToBitmapSource(cropped));
            }
            full.Dispose();
        }
        catch (Exception ex)
        {
            Logger.Log($"CaptureController.CaptureRegion failed: {ex.Message}");
        }
    }

    public async void CaptureFullScreen()
    {
        try
        {
            await HideMainWindowAndWaitAsync();

            using var full = ScreenCaptureService.CaptureVirtualScreen();
            Finish(ScreenCaptureService.ToBitmapSource(full));
        }
        catch (Exception ex)
        {
            Logger.Log($"CaptureController.CaptureFullScreen failed: {ex.Message}");
        }
    }

    public async void CaptureActiveWindow()
    {
        try
        {
            // Hide first: if our own main window was the foreground window, this lets Windows
            // hand focus back to whatever was behind it, which is what "active window" should mean.
            await HideMainWindowAndWaitAsync();

            var windowBounds = ScreenCaptureService.GetActiveWindowBounds();
            using var full = ScreenCaptureService.CaptureVirtualScreen();

            if (windowBounds is { } bounds)
            {
                using var cropped = ScreenCaptureService.Crop(full, bounds);
                Finish(ScreenCaptureService.ToBitmapSource(cropped));
            }
            else
            {
                Finish(ScreenCaptureService.ToBitmapSource(full));
            }
        }
        catch (Exception ex)
        {
            Logger.Log($"CaptureController.CaptureActiveWindow failed: {ex.Message}");
        }
    }

    private async Task HideMainWindowAndWaitAsync()
    {
        if (HideMainWindowIfVisible?.Invoke() == true)
        {
            // Give DWM/the window manager a moment to actually remove it from the screen (and,
            // for the active-window case, to hand focus to the next window) before grabbing pixels.
            await Task.Delay(150);
        }
    }

    private void Finish(BitmapSource image)
    {
        LastCapture = image;
        var settings = SettingsService.Current;

        if (settings.AutoCopyToClipboard) ClipboardService.SetImage(image);
        if (settings.PlayCaptureSound) ShutterSound.Play();

        if (settings.AutoOpenEditor)
        {
            var editor = EditorWindow.ForNewCapture(image);
            ConfigureNewEditorWindow?.Invoke(editor);
            editor.Show();
            editor.Activate();
        }
    }

    private static double GetPrimaryScale()
    {
        using var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero);
        return g.DpiX / 96.0;
    }
}
