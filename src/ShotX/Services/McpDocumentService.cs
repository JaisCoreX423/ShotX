using System.Drawing.Imaging;
using System.IO;
using System.Windows.Media.Imaging;
using ShotX.Controls;
using ShotX.Models;
using ShotX.Views;
using Color = System.Windows.Media.Color;
using Point = System.Windows.Point;
using Rect = System.Windows.Rect;
using Size = System.Windows.Size;

namespace ShotX.Services;

public sealed record DocumentInfo(string Id, int Width, int Height, int AnnotationCount);

/// <summary>
/// Backs the MCP server: owns the open "documents" (each one a real EditorWindow — capturing,
/// importing, annotating and saving through this all goes through the exact same code the UI
/// itself uses, so an AI client can't reach a state the app's own UI couldn't produce). Every
/// method that touches a WPF object hops onto the UI thread first, since the ASP.NET Core
/// request pipeline runs on its own thread pool.
/// </summary>
public class McpDocumentService
{
    private readonly Dictionary<string, EditorWindow> _documents = new();
    private int _nextId = 1;

    /// <summary>Hides the main launcher window if it's visible, same courtesy
    /// <see cref="CaptureController"/> already gives real hotkey-triggered captures — set once in
    /// App.xaml.cs.</summary>
    public Func<bool>? HideMainWindowIfVisible { get; set; }

    private static System.Windows.Threading.Dispatcher Dispatcher => System.Windows.Application.Current.Dispatcher;

    private static T RunOnUi<T>(Func<T> action)
    {
        try { return Dispatcher.Invoke(action); }
        catch (Exception ex)
        {
            Logger.Log($"McpDocumentService: UI-thread call failed: {ex}");
            throw;
        }
    }

    private static void RunOnUi(Action action)
    {
        try { Dispatcher.Invoke(action); }
        catch (Exception ex)
        {
            Logger.Log($"McpDocumentService: UI-thread call failed: {ex}");
            throw;
        }
    }

    private async Task HideMainWindowAndWaitAsync()
    {
        var hid = await Dispatcher.InvokeAsync(() => HideMainWindowIfVisible?.Invoke() == true);
        if (hid) await Task.Delay(150);
    }

    // ---------- Capture / import ----------

    public async Task<DocumentInfo> CaptureFullScreenAsync()
    {
        await HideMainWindowAndWaitAsync();
        return RunOnUi(() =>
        {
            using var full = ScreenCaptureService.CaptureVirtualScreen();
            return CreateDocument(ScreenCaptureService.ToBitmapSource(full));
        });
    }

    public async Task<DocumentInfo> CaptureRegionAsync(int x, int y, int width, int height)
    {
        await HideMainWindowAndWaitAsync();
        return RunOnUi(() =>
        {
            using var full = ScreenCaptureService.CaptureVirtualScreen();
            using var cropped = ScreenCaptureService.Crop(full, new System.Drawing.Rectangle(x, y, width, height));
            return CreateDocument(ScreenCaptureService.ToBitmapSource(cropped));
        });
    }

    public async Task<DocumentInfo> CaptureActiveWindowAsync()
    {
        await HideMainWindowAndWaitAsync();
        return RunOnUi(() =>
        {
            var bounds = ScreenCaptureService.GetActiveWindowBounds();
            using var full = ScreenCaptureService.CaptureVirtualScreen();
            BitmapSource image;
            if (bounds is { } b)
            {
                using var cropped = ScreenCaptureService.Crop(full, b);
                image = ScreenCaptureService.ToBitmapSource(cropped);
            }
            else
            {
                image = ScreenCaptureService.ToBitmapSource(full);
            }
            return CreateDocument(image);
        });
    }

    public DocumentInfo ImportImage(string path)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("No file at that path.", path);
        return RunOnUi(() =>
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(Path.GetFullPath(path));
            image.EndInit();
            image.Freeze();
            return CreateDocument(image);
        });
    }

    private DocumentInfo CreateDocument(BitmapSource image)
    {
        var id = $"doc-{_nextId++}";
        var editor = EditorWindow.ForNewCapture(image);
        _documents[id] = editor;
        editor.Closed += (_, _) => _documents.Remove(id);
        editor.Show();
        editor.Activate();
        return Describe(id, editor);
    }

    // ---------- Reading ----------

    public List<DocumentInfo> ListDocuments() =>
        RunOnUi(() => _documents.Select(kv => Describe(kv.Key, kv.Value)).ToList());

    /// <summary>Flattened PNG of the current canvas (background + every annotation baked in), as
    /// base64 — so the calling AI client can actually see the result, not just trust a status string.</summary>
    public string GetImageBase64(string documentId)
    {
        var editor = Require(documentId);
        return RunOnUi(() =>
        {
            var rtb = editor.Canvas.RenderFlattened();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(rtb));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            return Convert.ToBase64String(stream.ToArray());
        });
    }

    public string Save(string documentId, string? path)
    {
        var editor = Require(documentId);
        return RunOnUi(() => editor.SaveForMcp(path));
    }

    public void CloseDocument(string documentId, bool save)
    {
        var editor = Require(documentId);
        RunOnUi(() =>
        {
            if (save) editor.SaveForMcp();
            editor.Close();
        });
        _documents.Remove(documentId);
    }

    public void Undo(string documentId) => RunOnUi(() => Require(documentId).Canvas.Undo());
    public void Redo(string documentId) => RunOnUi(() => Require(documentId).Canvas.Redo());

    // ---------- Annotations ----------

    public void AddRectangle(string documentId, Rect bounds, Color color, double thickness, Color? fill, string text, Color? labelColor) =>
        RunOnUi(() => Require(documentId).Canvas.AddRectangle(bounds, color, thickness, fill, text, labelColor));

    public void AddEllipse(string documentId, Rect bounds, Color color, double thickness, Color? fill, string text, Color? labelColor) =>
        RunOnUi(() => Require(documentId).Canvas.AddEllipse(bounds, color, thickness, fill, text, labelColor));

    public void AddLine(string documentId, Point from, Point to, Color color, double thickness) =>
        RunOnUi(() => Require(documentId).Canvas.AddLine(from, to, color, thickness));

    public void AddArrow(string documentId, Point from, Point to, Color color, double thickness) =>
        RunOnUi(() => Require(documentId).Canvas.AddArrow(from, to, color, thickness));

    public void AddHighlighter(string documentId, IEnumerable<Point> points, Color color, double thickness) =>
        RunOnUi(() => Require(documentId).Canvas.AddHighlighter(points, color, thickness));

    public void AddText(string documentId, Rect bounds, string text, double fontSize, Color color, Color? background,
        TextHAlign hAlign, TextVAlign vAlign) =>
        RunOnUi(() => Require(documentId).Canvas.AddTextAnnotation(bounds, text, fontSize, color, background ?? default, hAlign, vAlign));

    public void AddStepNumber(string documentId, Point center, Color color) =>
        RunOnUi(() => Require(documentId).Canvas.AddStepNumber(center, color));

    public void AddBlurRegion(string documentId, Rect bounds) =>
        RunOnUi(() => Require(documentId).Canvas.AddBlurRegion(bounds));

    // ---------- Helpers ----------

    private EditorWindow Require(string documentId) =>
        _documents.TryGetValue(documentId, out var editor)
            ? editor
            : throw new KeyNotFoundException($"No open document with id '{documentId}'.");

    private static DocumentInfo Describe(string id, EditorWindow editor) =>
        new(id, (int)editor.Canvas.Background.PixelWidth, (int)editor.Canvas.Background.PixelHeight, editor.Canvas.Annotations.Count);
}
