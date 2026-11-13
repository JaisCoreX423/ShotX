using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;
using ShotX.Models;
using ShotX.Services;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Point = System.Windows.Point;
using Rect = System.Windows.Rect;

namespace ShotX.Mcp;

/// <summary>
/// Tools exposed to AI clients (Claude Code, Claude Desktop, etc.) over the local MCP server.
/// Every tool goes through McpDocumentService, which drives a real EditorWindow/DrawingCanvasControl
/// the exact same way the UI does — capturing, importing, annotating and saving here can't reach a
/// state the app's own UI couldn't produce. Coordinates are in the captured/imported image's own
/// pixels (0,0 = top-left), the same space the editor's layers use. Colors are hex strings, "#RRGGBB"
/// or "#AARRGGBB" (e.g. "#FF3B30" red, "#8000FF00" half-opaque green) — None/null means no fill/no
/// background where that's an option.
/// </summary>
[McpServerToolType]
public static class ShotXMcpTools
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() }
    };

    private static string ToJson(object value) => JsonSerializer.Serialize(value, JsonOptions);

    private static Color ParseColor(string? hex, Color fallback)
    {
        if (string.IsNullOrWhiteSpace(hex)) return fallback;
        try { return (Color)ColorConverter.ConvertFromString(hex)!; }
        catch { return fallback; }
    }

    private static Color? ParseOptionalColor(string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return null;
        try { return (Color)ColorConverter.ConvertFromString(hex)!; }
        catch { return null; }
    }

    private static readonly Color DefaultStroke = Color.FromRgb(0xFF, 0x3B, 0x30);

    // ---------- Capture / import ----------

    [McpServerTool, Description(
        "Capture the full virtual desktop (all monitors) and open it as a new ShotX document. " +
        "Returns the new document's id, pixel size, and annotation count — use the id with every " +
        "other tool. ShotX's own main window is hidden first so it never ends up in the shot.")]
    public static async Task<string> CaptureFullScreen(McpDocumentService service) =>
        ToJson(await service.CaptureFullScreenAsync());

    [McpServerTool, Description(
        "Capture a specific rectangular region of the virtual desktop (device pixels, origin at the " +
        "top-left of the primary monitor — can be negative on a multi-monitor setup) and open it as a " +
        "new ShotX document.")]
    public static async Task<string> CaptureRegion(McpDocumentService service, int x, int y, int width, int height) =>
        ToJson(await service.CaptureRegionAsync(x, y, width, height));

    [McpServerTool, Description(
        "Capture whatever window is currently in the foreground and open it as a new ShotX document. " +
        "ShotX's own main window is hidden first, so if it was the foreground window this captures " +
        "whatever was behind it instead.")]
    public static async Task<string> CaptureActiveWindow(McpDocumentService service) =>
        ToJson(await service.CaptureActiveWindowAsync());

    [McpServerTool, Description(
        "Open an existing image file (png/jpg/bmp) from disk as a new ShotX document, so it can be " +
        "annotated the same as a fresh capture.")]
    public static string ImportImage(McpDocumentService service, string path)
    {
        try { return ToJson(service.ImportImage(path)); }
        catch (Exception ex) { return ToJson(new { error = "import_failed", detail = ex.Message }); }
    }

    [McpServerTool, Description("List every ShotX document currently open (id, pixel size, annotation count).")]
    public static string ListDocuments(McpDocumentService service) => ToJson(service.ListDocuments());

    [McpServerTool, Description(
        "Get the document's current rendered image (background + every annotation baked in) as a " +
        "base64-encoded PNG. Prefer `save` and reading the saved file when you just need to look at " +
        "the result — this is for when you need the raw pixels without writing a file.")]
    public static string GetImage(McpDocumentService service, string documentId)
    {
        try { return ToJson(new { base64Png = service.GetImageBase64(documentId) }); }
        catch (Exception ex) { return ToJson(new { error = "not_found", detail = ex.Message }); }
    }

    [McpServerTool, Description(
        "Save a document (background + all annotations flattened) as a PNG or JPEG. Omit path to use " +
        "ShotX's configured screenshots folder with an auto-generated name. Returns the path actually used.")]
    public static string Save(McpDocumentService service, string documentId, string? path = null)
    {
        try { return ToJson(new { path = service.Save(documentId, path) }); }
        catch (Exception ex) { return ToJson(new { error = "save_failed", detail = ex.Message }); }
    }

    [McpServerTool, Description(
        "Close a document's editor window. Set save=true to save it first (same as the `save` tool, " +
        "default path).")]
    public static string CloseDocument(McpDocumentService service, string documentId, bool save = false)
    {
        try { service.CloseDocument(documentId, save); return ToJson(new { ok = true }); }
        catch (Exception ex) { return ToJson(new { error = "not_found", detail = ex.Message }); }
    }

    [McpServerTool, Description("Undo the last annotation change on a document.")]
    public static string Undo(McpDocumentService service, string documentId)
    {
        try { service.Undo(documentId); return ToJson(new { ok = true }); }
        catch (Exception ex) { return ToJson(new { error = "not_found", detail = ex.Message }); }
    }

    [McpServerTool, Description("Redo the last undone annotation change on a document.")]
    public static string Redo(McpDocumentService service, string documentId)
    {
        try { service.Redo(documentId); return ToJson(new { ok = true }); }
        catch (Exception ex) { return ToJson(new { error = "not_found", detail = ex.Message }); }
    }

    // ---------- Annotations ----------

    [McpServerTool, Description(
        "Draw a rectangle outline (optionally filled) on a document, at (x,y) with the given size, " +
        "in the image's own pixels. Optionally give it a centered text label (its own color, " +
        "independent of the outline) — same as typing into a rectangle in the editor.")]
    public static string AddRectangle(McpDocumentService service, string documentId,
        double x, double y, double width, double height,
        string color = "#FF3B30", double thickness = 4, string? fillColor = null,
        string text = "", string? labelColor = null)
    {
        try
        {
            service.AddRectangle(documentId, new Rect(x, y, width, height), ParseColor(color, DefaultStroke),
                thickness, ParseOptionalColor(fillColor), text, ParseOptionalColor(labelColor));
            return ToJson(new { ok = true });
        }
        catch (Exception ex) { return ToJson(new { error = "not_found", detail = ex.Message }); }
    }

    [McpServerTool, Description(
        "Draw an ellipse outline (optionally filled) on a document, inscribed in the (x,y,width,height) " +
        "box. Optionally give it a centered text label, same as AddRectangle.")]
    public static string AddEllipse(McpDocumentService service, string documentId,
        double x, double y, double width, double height,
        string color = "#FF3B30", double thickness = 4, string? fillColor = null,
        string text = "", string? labelColor = null)
    {
        try
        {
            service.AddEllipse(documentId, new Rect(x, y, width, height), ParseColor(color, DefaultStroke),
                thickness, ParseOptionalColor(fillColor), text, ParseOptionalColor(labelColor));
            return ToJson(new { ok = true });
        }
        catch (Exception ex) { return ToJson(new { error = "not_found", detail = ex.Message }); }
    }

    [McpServerTool, Description("Draw a straight line from (x1,y1) to (x2,y2) on a document.")]
    public static string AddLine(McpDocumentService service, string documentId,
        double x1, double y1, double x2, double y2, string color = "#FF3B30", double thickness = 4)
    {
        try
        {
            service.AddLine(documentId, new Point(x1, y1), new Point(x2, y2), ParseColor(color, DefaultStroke), thickness);
            return ToJson(new { ok = true });
        }
        catch (Exception ex) { return ToJson(new { error = "not_found", detail = ex.Message }); }
    }

    [McpServerTool, Description("Draw an arrow from (x1,y1) to (x2,y2) — the arrowhead points at (x2,y2) — on a document.")]
    public static string AddArrow(McpDocumentService service, string documentId,
        double x1, double y1, double x2, double y2, string color = "#FF3B30", double thickness = 4)
    {
        try
        {
            service.AddArrow(documentId, new Point(x1, y1), new Point(x2, y2), ParseColor(color, DefaultStroke), thickness);
            return ToJson(new { ok = true });
        }
        catch (Exception ex) { return ToJson(new { error = "not_found", detail = ex.Message }); }
    }

    [McpServerTool, Description(
        "Draw a translucent highlighter stroke through a sequence of points (flat list: x1,y1,x2,y2,...), " +
        "at least two points, on a document.")]
    public static string AddHighlighter(McpDocumentService service, string documentId,
        double[] points, string color = "#FFCC00", double thickness = 12)
    {
        try
        {
            if (points.Length < 4 || points.Length % 2 != 0)
                return ToJson(new { error = "invalid_points", detail = "Need an even-length flat list of at least 2 points (x,y pairs)." });

            var pts = new List<Point>();
            for (var i = 0; i < points.Length; i += 2) pts.Add(new Point(points[i], points[i + 1]));
            service.AddHighlighter(documentId, pts, ParseColor(color, Color.FromRgb(0xFF, 0xCC, 0x00)), thickness);
            return ToJson(new { ok = true });
        }
        catch (Exception ex) { return ToJson(new { error = "not_found", detail = ex.Message }); }
    }

    [McpServerTool, Description(
        "Add a standalone text label at (x,y) with the given box size, on a document. hAlign is " +
        "Left|Center|Right, vAlign is Top|Center|Bottom (both default Center). backgroundColor is " +
        "optional (a filled chip behind the text); omit for none.")]
    public static string AddText(McpDocumentService service, string documentId,
        double x, double y, double width, double height, string text,
        double fontSize = 20, string color = "#1D1D1F", string? backgroundColor = null,
        string hAlign = "Center", string vAlign = "Center")
    {
        try
        {
            if (!Enum.TryParse<TextHAlign>(hAlign, true, out var h)) h = TextHAlign.Center;
            if (!Enum.TryParse<TextVAlign>(vAlign, true, out var v)) v = TextVAlign.Center;
            service.AddText(documentId, new Rect(x, y, width, height), text, fontSize,
                ParseColor(color, Color.FromRgb(0x1D, 0x1D, 0x1F)), ParseOptionalColor(backgroundColor), h, v);
            return ToJson(new { ok = true });
        }
        catch (Exception ex) { return ToJson(new { error = "not_found", detail = ex.Message }); }
    }

    [McpServerTool, Description("Add a numbered step marker (auto-incrementing: 1, 2, 3, ...) centered at (x,y) on a document.")]
    public static string AddStepNumber(McpDocumentService service, string documentId, double x, double y, string color = "#FF3B30")
    {
        try
        {
            service.AddStepNumber(documentId, new Point(x, y), ParseColor(color, DefaultStroke));
            return ToJson(new { ok = true });
        }
        catch (Exception ex) { return ToJson(new { error = "not_found", detail = ex.Message }); }
    }

    [McpServerTool, Description("Pixelate/blur a rectangular region (x,y,width,height) on a document, e.g. to redact sensitive text.")]
    public static string AddBlurRegion(McpDocumentService service, string documentId, double x, double y, double width, double height)
    {
        try
        {
            service.AddBlurRegion(documentId, new Rect(x, y, width, height));
            return ToJson(new { ok = true });
        }
        catch (Exception ex) { return ToJson(new { error = "not_found", detail = ex.Message }); }
    }
}
