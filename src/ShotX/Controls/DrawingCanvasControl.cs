using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ShotX.Models;
using ShotX.Services;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Pen = System.Windows.Media.Pen;
using Size = System.Windows.Size;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace ShotX.Controls;

/// <summary>
/// The editor's image + annotation surface: owns the shape list, hit-testing, undo/redo, and
/// all rendering. One instance per open screenshot.
/// </summary>
public class DrawingCanvasControl : FrameworkElement
{
    private const double HandleSize = 9;
    private const double HitTolerance = 7;

    private BitmapSource _background;
    private readonly List<Annotation> _annotations = new();
    private readonly Stack<List<Annotation>> _undoStack = new();
    private readonly Stack<List<Annotation>> _redoStack = new();

    private Annotation? _inProgress;
    private Annotation? _selected;
    private bool _isDraggingPoint;   // dragging an endpoint of a line/arrow
    private int _draggingPointIndex = -1;
    private bool _isMoving;
    private bool _isResizing;
    private Point _lastMouse;
    private Rect? _pendingCrop;
    private int _nextStepNumber = 1;

    public DrawingCanvasControl(BitmapSource background)
    {
        _background = background;
        Width = background.PixelWidth;
        Height = background.PixelHeight;
        Focusable = true;
        ClipToBounds = true;
    }

    public ToolType CurrentTool { get; set; } = ToolType.Select;
    public Color StrokeColor { get; set; } = Colors.Red;
    public double StrokeThickness { get; set; } = 4;

    /// <summary>Fill used for newly created Rectangle/Ellipse shapes (alpha 0 = no fill, outline
    /// only). Also the default background for newly created Text.</summary>
    public Color FillColor { get; set; } = Colors.Transparent;
    public Rect? PendingCrop => _pendingCrop;

    public event EventHandler? Changed;
    public event Action<Point>? TextToolClicked;
    public event Action<Rect?>? CropProposed;
    public event Action<Annotation>? TextEditRequested;

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;
    public bool HasSelection => _selected != null;

    public BitmapSource Background => _background;

    /// <summary>Adds one of each annotation type directly (bypassing mouse-driven creation), for
    /// visual regression snapshots only.</summary>
    /// <summary>Simulates being mid-drag with the Crop tool (mouse still down), to verify the
    /// live dim/selection preview renders before the mouse is released.</summary>
    public void SimulateCropDragInProgressForTesting(Rect bounds)
    {
        CurrentTool = ToolType.Crop;
        _inProgress = new Annotation { Type = ToolType.Crop, Bounds = bounds };
        InvalidateVisual();
    }

    /// <summary>Drives a multi-step Rectangle/Ellipse drag through the exact same bounds-normalizing
    /// logic <see cref="OnMouseMove"/> uses (<see cref="NormalizeDragBounds"/>), one mouse-move step
    /// per point in <paramref name="path"/> — reproduces a real drag's incremental calls instead of
    /// just setting the final Bounds directly, which is what let the left/up-drag bug through
    /// undetected originally (every prior test only ever set Bounds once, in a single step).</summary>
    public void SimulateMultiStepShapeDragForTesting(ToolType tool, Point down, params Point[] path)
    {
        CurrentTool = tool;
        _lastMouse = down;
        _inProgress = new Annotation
        {
            Type = tool,
            Color = StrokeColor,
            Thickness = StrokeThickness,
            Bounds = new Rect(down, new Size(0, 0)),
            BackgroundColor = tool is ToolType.Rectangle or ToolType.Ellipse ? FillColor : Colors.Transparent
        };
        foreach (var p in path) _inProgress.Bounds = NormalizeDragBounds(p);
    }

    public Rect? InProgressBoundsForTesting => _inProgress?.Bounds;

    /// <summary>Finishes the shape started by <see cref="SimulateMultiStepShapeDragForTesting"/>,
    /// same finalization <see cref="OnMouseLeftButtonUp"/> applies (too-small-to-keep check, add to
    /// the annotation list, select it).</summary>
    public void FinishMultiStepShapeDragForTesting()
    {
        if (_inProgress == null) return;
        var finished = _inProgress;
        _inProgress = null;
        if (finished.Bounds.Width < 3 || finished.Bounds.Height < 3) return;
        PushUndo();
        _annotations.Add(finished);
        _selected = finished;
        Raise();
    }

    /// <summary>Adds one rectangle using the canvas's current StrokeColor/StrokeThickness/FillColor,
    /// as a stand-in for actually drawing one with the mouse — used only to verify those "current
    /// defaults" (incl. persisted ones) actually reach a newly created shape.</summary>
    public void AddRectangleWithCurrentDefaultsForTesting()
    {
        var annotation = new Annotation
        {
            Type = ToolType.Rectangle,
            Bounds = new Rect(40, 420, 160, 90),
            Color = StrokeColor,
            Thickness = StrokeThickness,
            BackgroundColor = FillColor
        };
        _annotations.Add(annotation);
        _selected = annotation;
        Raise();
    }

    public void AddSampleAnnotationsForTesting()
    {
        _annotations.Add(new Annotation { Type = ToolType.Rectangle, Bounds = new Rect(40, 40, 160, 90), Color = Colors.Red, Thickness = 4 });
        _annotations.Add(new Annotation { Type = ToolType.Ellipse, Bounds = new Rect(230, 40, 140, 90), Color = Color.FromRgb(0x34, 0xC7, 0x59), Thickness = 4 });
        _annotations.Add(new Annotation { Type = ToolType.Arrow, Points = new List<Point> { new(420, 60), new(560, 120) }, Color = Color.FromRgb(0x0A, 0x84, 0xFF), Thickness = 4 });
        _annotations.Add(new Annotation { Type = ToolType.Line, Points = new List<Point> { new(600, 50), new(700, 130) }, Color = Colors.Black, Thickness = 3 });
        _annotations.Add(new Annotation { Type = ToolType.Pen, Points = new List<Point> { new(40, 180), new(80, 220), new(120, 170), new(160, 230), new(200, 190) }, Color = Color.FromRgb(0xAF, 0x52, 0xDE), Thickness = 4 });
        _annotations.Add(new Annotation { Type = ToolType.Highlighter, Points = new List<Point> { new(230, 200), new(420, 200) }, Color = Color.FromRgb(0xFF, 0xCC, 0x00), Thickness = 4 });
        _annotations.Add(new Annotation { Type = ToolType.Text, Bounds = new Rect(460, 170, 200, 40), Text = "Texto de prueba", Color = Colors.Black, FontSize = 22 });
        _annotations.Add(new Annotation { Type = ToolType.StepNumber, Bounds = new Rect(40, 260, 28, 28), Color = Color.FromRgb(0xFF, 0x3B, 0x30), StepIndex = 1 });
        _annotations.Add(new Annotation { Type = ToolType.StepNumber, Bounds = new Rect(90, 260, 28, 28), Color = Color.FromRgb(0xFF, 0x3B, 0x30), StepIndex = 2 });

        var blur = new Annotation { Type = ToolType.Blur, Bounds = new Rect(200, 350, 150, 90), Thickness = 8 };
        RecomputeBlur(blur);
        _annotations.Add(blur);

        _selected = _annotations[0];
        Raise();
    }

    // ---------- Layers ----------

    /// <summary>Back-to-front order (index 0 = furthest back), same order they render in.</summary>
    public IReadOnlyList<Annotation> Annotations => _annotations;

    public Annotation? Selected => _selected;

    public void SelectAnnotationExternally(Annotation? annotation)
    {
        _selected = annotation;
        InvalidateVisual();
    }

    public void ToggleLock(Annotation annotation)
    {
        annotation.Locked = !annotation.Locked;
        Raise();
    }

    public void DeleteAnnotationExternally(Annotation annotation)
    {
        PushUndo();
        _annotations.Remove(annotation);
        if (_selected == annotation) _selected = null;
        Raise();
    }

    /// <summary>Moves a layer one step toward the front (+1) or back (-1) of the stack.</summary>
    public void MoveLayer(Annotation annotation, int direction)
    {
        var index = _annotations.IndexOf(annotation);
        var target = index + direction;
        if (index < 0 || target < 0 || target >= _annotations.Count) return;

        PushUndo();
        (_annotations[index], _annotations[target]) = (_annotations[target], _annotations[index]);
        Raise();
    }

    /// <summary>Drag-and-drop reorder: moves <paramref name="moved"/> to sit where
    /// <paramref name="target"/> currently is in the layer stack.</summary>
    public void ReorderAnnotation(Annotation moved, Annotation target)
    {
        var fromIndex = _annotations.IndexOf(moved);
        var toIndex = _annotations.IndexOf(target); // captured before the removal below shifts anything
        if (fromIndex < 0 || toIndex < 0 || fromIndex == toIndex) return;

        PushUndo();
        _annotations.RemoveAt(fromIndex);
        // Re-using the pre-removal index is intentional, not a bug: for an item immediately
        // adjacent to the target this lands just past it (a clean swap) instead of collapsing
        // back to the original order, which is what recomputing the index after removal would do.
        _annotations.Insert(toIndex, moved);
        Raise();
    }

    public void AddImageAnnotation(BitmapSource image)
    {
        PushUndo();

        var maxW = Math.Max(80, ActualWidth * 0.4);
        var maxH = Math.Max(80, ActualHeight * 0.4);
        var scale = Math.Min(1, Math.Min(maxW / image.PixelWidth, maxH / image.PixelHeight));
        var w = image.PixelWidth * scale;
        var h = image.PixelHeight * scale;
        var bounds = new Rect((ActualWidth - w) / 2, (ActualHeight - h) / 2, w, h);

        var annotation = new Annotation { Type = ToolType.Image, Bounds = bounds, Image = image };
        _annotations.Add(annotation);
        _selected = annotation;
        Raise();
    }

    public void UpdateTextAnnotation(Annotation annotation, string text, Rect bounds, double fontSize, Color color,
        Color backgroundColor, TextHAlign hAlign, TextVAlign vAlign)
    {
        PushUndo();
        annotation.Text = text;
        annotation.Bounds = bounds;
        annotation.FontSize = fontSize;
        annotation.Color = color;
        annotation.BackgroundColor = backgroundColor;
        annotation.TextHAlign = hAlign;
        annotation.TextVAlign = vAlign;
        _selected = annotation;
        Raise();
    }

    /// <summary>Nudges the selected annotation's stroke width (or blur intensity) live, e.g. from
    /// the toolbar's numeric stepper, in addition to that stepper setting the default for new shapes.</summary>
    public void ApplyThicknessToSelection(double thickness)
    {
        if (_selected is not { Locked: false }) return;
        if (_selected.Type is ToolType.StepNumber or ToolType.Text or ToolType.Image) return;

        PushUndo();
        _selected.Thickness = thickness;
        if (_selected.Type == ToolType.Blur) RecomputeBlur(_selected);
        Raise();
    }

    /// <summary>Nudges the selected text annotation's font size live, e.g. from the toolbar's
    /// numeric stepper, in addition to that stepper setting the default for new text.</summary>
    public void ApplyFontSizeToSelection(double fontSize)
    {
        if (_selected is not { Locked: false, Type: ToolType.Text }) return;

        PushUndo();
        _selected.FontSize = fontSize;
        Raise();
    }

    /// <summary>Nudges the selected annotation's stroke/fill/text color live, e.g. from the
    /// toolbar's color swatches, in addition to that swatch setting the default for new shapes.</summary>
    public void ApplyColorToSelection(Color color)
    {
        if (_selected is not { Locked: false } selected) return;
        if (selected.Type is ToolType.Image) return;

        PushUndo();
        selected.Color = color;
        Raise();
    }

    private static readonly ToolType[] LabeledShapeTypes = { ToolType.Rectangle, ToolType.Ellipse };

    /// <summary>Sets the color of the label typed inside the selected Rectangle/Ellipse, independent
    /// of that shape's own outline color (<see cref="ApplyColorToSelection"/>).</summary>
    public void ApplyLabelColorToSelection(Color color)
    {
        if (_selected is not { Locked: false } selected || Array.IndexOf(LabeledShapeTypes, selected.Type) < 0) return;

        PushUndo();
        selected.LabelColor = color;
        Raise();
    }

    private static readonly ToolType[] FillableTypes = { ToolType.Text, ToolType.Rectangle, ToolType.Ellipse };

    /// <summary>Sets the selected text/rectangle/ellipse layer's fill live (alpha 0 = no fill,
    /// outline only for shapes / no chip for text).</summary>
    public void ApplyFillToSelection(Color backgroundColor)
    {
        if (_selected is not { Locked: false } selected || Array.IndexOf(FillableTypes, selected.Type) < 0) return;

        PushUndo();
        selected.BackgroundColor = backgroundColor;
        Raise();
    }

    private static readonly ToolType[] AlignableTypes = { ToolType.Text, ToolType.Rectangle, ToolType.Ellipse };

    /// <summary>Sets horizontal and/or vertical text alignment on the selected text/rectangle/ellipse
    /// layer; pass null for whichever axis shouldn't change.</summary>
    public void ApplyTextAlignToSelection(TextHAlign? hAlign = null, TextVAlign? vAlign = null)
    {
        if (_selected is not { Locked: false } selected || Array.IndexOf(AlignableTypes, selected.Type) < 0) return;

        PushUndo();
        if (hAlign.HasValue) selected.TextHAlign = hAlign.Value;
        if (vAlign.HasValue) selected.TextVAlign = vAlign.Value;
        Raise();
    }

    /// <summary>Updates the text label embedded in a Rectangle/Ellipse (as opposed to a standalone
    /// Text annotation) without touching its bounds, color or fill.</summary>
    public void UpdateShapeText(Annotation annotation, string text, double fontSize, TextHAlign hAlign, TextVAlign vAlign)
    {
        PushUndo();
        annotation.Text = text;
        annotation.FontSize = fontSize;
        annotation.TextHAlign = hAlign;
        annotation.TextVAlign = vAlign;
        _selected = annotation;
        Raise();
    }

    // ---------- Clipboard (in-app, for cloning shapes — distinct from the OS image clipboard) ----------

    private Annotation? _internalClipboard;

    private static void OffsetAnnotation(Annotation a, Vector delta)
    {
        a.Bounds = new Rect(a.Bounds.TopLeft + delta, a.Bounds.Size);
        a.Points = a.Points.Select(p => p + delta).ToList();
    }

    public void CopySelectedToClipboard()
    {
        if (_selected == null) return;
        _internalClipboard = _selected.Clone();
    }

    public void PasteFromClipboard()
    {
        if (_internalClipboard == null) return;
        PushUndo();
        var copy = _internalClipboard.Clone();
        OffsetAnnotation(copy, new Vector(16, 16));
        _annotations.Add(copy);
        _selected = copy;
        Raise();
    }

    public void DuplicateSelected()
    {
        if (_selected is not { } selected) return;
        PushUndo();
        var copy = selected.Clone();
        OffsetAnnotation(copy, new Vector(16, 16));
        _annotations.Add(copy);
        _selected = copy;
        Raise();
    }

    // ---------- Undo / redo ----------

    private void PushUndo()
    {
        _undoStack.Push(_annotations.Select(a => a.Clone()).ToList());
        _redoStack.Clear();
    }

    public void Undo()
    {
        if (_undoStack.Count == 0) return;
        _redoStack.Push(_annotations.Select(a => a.Clone()).ToList());
        var previous = _undoStack.Pop();
        _annotations.Clear();
        _annotations.AddRange(previous);
        _selected = null;
        Raise();
    }

    public void Redo()
    {
        if (_redoStack.Count == 0) return;
        _undoStack.Push(_annotations.Select(a => a.Clone()).ToList());
        var next = _redoStack.Pop();
        _annotations.Clear();
        _annotations.AddRange(next);
        _selected = null;
        Raise();
    }

    private void Raise()
    {
        Changed?.Invoke(this, EventArgs.Empty);
        InvalidateVisual();
    }

    // ---------- Text tool (invoked externally after inline edit box commits) ----------

    public void AddTextAnnotation(Rect bounds, string text, double fontSize, Color color, Color backgroundColor,
        TextHAlign hAlign, TextVAlign vAlign)
    {
        if (string.IsNullOrEmpty(text)) return;
        PushUndo();
        var annotation = new Annotation
        {
            Type = ToolType.Text,
            Bounds = bounds,
            Text = text,
            FontSize = fontSize,
            Color = color,
            BackgroundColor = backgroundColor,
            TextHAlign = hAlign,
            TextVAlign = vAlign
        };
        _annotations.Add(annotation);
        _selected = annotation;
        Raise();
    }

    // ---------- Crop ----------

    public void ApplyCrop(Rect cropRect)
    {
        cropRect.Intersect(new Rect(0, 0, _background.PixelWidth, _background.PixelHeight));
        if (cropRect.Width < 2 || cropRect.Height < 2) { _pendingCrop = null; CropProposed?.Invoke(null); Raise(); return; }

        PushUndo();
        var px = new Int32Rect((int)cropRect.X, (int)cropRect.Y, (int)cropRect.Width, (int)cropRect.Height);
        _background = new CroppedBitmap(_background, px);
        Width = _background.PixelWidth;
        Height = _background.PixelHeight;

        var offset = new Vector(-cropRect.X, -cropRect.Y);
        var kept = new List<Annotation>();
        foreach (var a in _annotations)
        {
            var moved = a.Clone();
            moved.Bounds = new Rect(a.Bounds.TopLeft + offset, a.Bounds.Size);
            moved.Points = a.Points.Select(p => p + offset).ToList();
            kept.Add(moved);
        }
        _annotations.Clear();
        _annotations.AddRange(kept);

        _pendingCrop = null;
        CropProposed?.Invoke(null);
        Raise();
    }

    public void CancelCrop()
    {
        _pendingCrop = null;
        CropProposed?.Invoke(null);
        Raise();
    }

    // ---------- Export ----------

    public RenderTargetBitmap RenderFlattened()
    {
        var dpi = 96d;
        var rtb = new RenderTargetBitmap(_background.PixelWidth, _background.PixelHeight, dpi, dpi, PixelFormats.Pbgra32);
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            dc.DrawImage(_background, new Rect(0, 0, _background.PixelWidth, _background.PixelHeight));
            foreach (var a in _annotations) DrawAnnotation(dc, a);
        }
        rtb.Render(visual);
        rtb.Freeze();
        return rtb;
    }

    // ---------- Rendering ----------

    protected override void OnRender(DrawingContext dc)
    {
        dc.DrawImage(_background, new Rect(0, 0, ActualWidth, ActualHeight));

        foreach (var a in _annotations)
        {
            DrawAnnotation(dc, a);
            if (a == _selected) DrawSelectionChrome(dc, a);
        }

        if (_inProgress != null && _inProgress.Type != ToolType.Crop) DrawAnnotation(dc, _inProgress);

        // Crop shows the dim-outside/clear-inside selection live while dragging, not just after
        // releasing the mouse — otherwise there's no feedback at all until you let go.
        var cropPreview = _pendingCrop ?? (_inProgress?.Type == ToolType.Crop ? _inProgress.Bounds : (Rect?)null);
        if (cropPreview is { } crop)
        {
            var outside = new GeometryGroup { FillRule = FillRule.EvenOdd };
            outside.Children.Add(new RectangleGeometry(new Rect(0, 0, ActualWidth, ActualHeight)));
            outside.Children.Add(new RectangleGeometry(crop));
            dc.DrawGeometry(new SolidColorBrush(Color.FromArgb(140, 0, 0, 0)), null, outside);
            dc.DrawRectangle(null, new Pen(Brushes.DeepSkyBlue, 2), crop);
        }
    }

    private void DrawAnnotation(DrawingContext dc, Annotation a)
    {
        var brush = new SolidColorBrush(a.Color);
        var pen = new Pen(brush, a.Thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };

        switch (a.Type)
        {
            case ToolType.Rectangle:
                if (a.BackgroundColor.A > 0) dc.DrawRoundedRectangle(new SolidColorBrush(a.BackgroundColor), null, a.Bounds, 3, 3);
                dc.DrawRoundedRectangle(null, pen, a.Bounds, 3, 3);
                if (!string.IsNullOrEmpty(a.Text)) DrawAlignedText(dc, a, new SolidColorBrush(a.LabelColor), a.Bounds);
                break;

            case ToolType.Ellipse:
                var ellipseCenter = new Point(a.Bounds.X + a.Bounds.Width / 2, a.Bounds.Y + a.Bounds.Height / 2);
                if (a.BackgroundColor.A > 0) dc.DrawEllipse(new SolidColorBrush(a.BackgroundColor), null, ellipseCenter, a.Bounds.Width / 2, a.Bounds.Height / 2);
                dc.DrawEllipse(null, pen, ellipseCenter, a.Bounds.Width / 2, a.Bounds.Height / 2);
                if (!string.IsNullOrEmpty(a.Text))
                {
                    // Inset so text wraps within the ellipse's inscribed rectangle, not its bounding box.
                    var inset = new Rect(a.Bounds.X + a.Bounds.Width * 0.146, a.Bounds.Y + a.Bounds.Height * 0.146,
                        a.Bounds.Width * 0.708, a.Bounds.Height * 0.708);
                    DrawAlignedText(dc, a, new SolidColorBrush(a.LabelColor), inset);
                }
                break;

            case ToolType.Line:
                if (a.Points.Count >= 2) dc.DrawLine(pen, a.Points[0], a.Points[1]);
                break;

            case ToolType.Arrow:
                if (a.Points.Count >= 2) DrawArrow(dc, a.Points[0], a.Points[1], brush, a.Thickness);
                break;

            case ToolType.Pen:
                DrawPolyline(dc, pen, a.Points);
                break;

            case ToolType.Highlighter:
                var hPen = new Pen(new SolidColorBrush(Color.FromArgb(110, a.Color.R, a.Color.G, a.Color.B)),
                    Math.Max(a.Thickness * 3, 12)) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
                DrawPolyline(dc, hPen, a.Points);
                break;

            case ToolType.Text:
                if (a.BackgroundColor.A > 0)
                {
                    dc.DrawRoundedRectangle(new SolidColorBrush(a.BackgroundColor), null, a.Bounds, 4, 4);
                }
                DrawAlignedText(dc, a, brush, a.Bounds);
                break;

            case ToolType.StepNumber:
                var radius = a.Bounds.Width / 2;
                var center = new Point(a.Bounds.X + radius, a.Bounds.Y + radius);
                dc.DrawEllipse(brush, null, center, radius, radius);
                var numberText = new FormattedText(a.StepIndex.ToString(), System.Globalization.CultureInfo.CurrentCulture,
                    System.Windows.FlowDirection.LeftToRight, new Typeface("Segoe UI Semibold"), radius * 1.1, Brushes.White, 1.25);
                dc.DrawText(numberText, new Point(center.X - numberText.Width / 2, center.Y - numberText.Height / 2));
                break;

            case ToolType.Blur:
                if (a.BlurTile != null) dc.DrawImage(a.BlurTile, a.Bounds);
                else dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(160, 120, 120, 120)), null, a.Bounds);
                break;

            case ToolType.Image:
                if (a.Image != null) dc.DrawImage(a.Image, a.Bounds);
                break;
        }
    }

    private const double TextPadding = 4;

    /// <summary>Draws <paramref name="a"/>.Text word-wrapped within <paramref name="box"/>, positioned
    /// per <see cref="Annotation.TextHAlign"/>/<see cref="Annotation.TextVAlign"/>.</summary>
    private static void DrawAlignedText(DrawingContext dc, Annotation a, Brush brush, Rect box)
    {
        var maxWidth = Math.Max(1, box.Width - TextPadding * 2);
        var formatted = new FormattedText(a.Text, System.Globalization.CultureInfo.CurrentCulture,
            System.Windows.FlowDirection.LeftToRight, new Typeface("Segoe UI"), a.FontSize, brush, 1.25)
        {
            MaxTextWidth = maxWidth,
            TextAlignment = a.TextHAlign switch
            {
                TextHAlign.Left => System.Windows.TextAlignment.Left,
                TextHAlign.Right => System.Windows.TextAlignment.Right,
                _ => System.Windows.TextAlignment.Center
            }
        };

        var x = box.Left + TextPadding;
        var y = a.TextVAlign switch
        {
            TextVAlign.Top => box.Top + TextPadding,
            TextVAlign.Bottom => box.Bottom - TextPadding - formatted.Height,
            _ => box.Top + (box.Height - formatted.Height) / 2
        };
        dc.DrawText(formatted, new Point(x, y));
    }

    private static void DrawPolyline(DrawingContext dc, Pen pen, List<Point> points)
    {
        if (points.Count < 2) return;
        var geo = new StreamGeometry();
        using (var ctx = geo.Open())
        {
            ctx.BeginFigure(points[0], false, false);
            ctx.PolyLineTo(points.Skip(1).ToList(), true, true);
        }
        dc.DrawGeometry(null, pen, geo);
    }

    private static void DrawArrow(DrawingContext dc, Point from, Point to, Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        dc.DrawLine(pen, from, to);

        var angle = Math.Atan2(to.Y - from.Y, to.X - from.X);
        var headLength = 8 + thickness * 2.5;
        var headAngle = Math.PI / 7;
        var p1 = new Point(to.X - headLength * Math.Cos(angle - headAngle), to.Y - headLength * Math.Sin(angle - headAngle));
        var p2 = new Point(to.X - headLength * Math.Cos(angle + headAngle), to.Y - headLength * Math.Sin(angle + headAngle));

        var headGeo = new StreamGeometry();
        using (var ctx = headGeo.Open())
        {
            ctx.BeginFigure(to, true, true);
            ctx.LineTo(p1, true, true);
            ctx.LineTo(p2, true, true);
        }
        dc.DrawGeometry(brush, null, headGeo);
    }

    private void DrawSelectionChrome(DrawingContext dc, Annotation a)
    {
        var dash = new Pen(Brushes.DeepSkyBlue, 1) { DashStyle = new DashStyle(new double[] { 3, 2 }, 0) };

        if (a.Type is ToolType.Line or ToolType.Arrow && a.Points.Count >= 2)
        {
            if (!a.Locked) foreach (var p in a.Points) DrawHandle(dc, p);
            return;
        }

        dc.DrawRectangle(null, dash, a.Bounds);
        if (!a.Locked && a.Type is not ToolType.StepNumber)
        {
            DrawHandle(dc, new Point(a.Bounds.Right, a.Bounds.Bottom));
        }
    }

    private static void DrawHandle(DrawingContext dc, Point p)
    {
        var r = HandleSize / 2;
        dc.DrawEllipse(Brushes.White, new Pen(Brushes.DeepSkyBlue, 1.5), p, r, r);
    }

    // ---------- Hit testing ----------

    private Annotation? HitTest(Point p)
    {
        for (var i = _annotations.Count - 1; i >= 0; i--)
        {
            var a = _annotations[i];
            if (!a.Locked && IsHit(a, p)) return a;
        }
        return null;
    }

    private static bool IsHit(Annotation a, Point p)
    {
        switch (a.Type)
        {
            case ToolType.Line:
            case ToolType.Arrow:
                return a.Points.Count >= 2 && DistanceToSegment(p, a.Points[0], a.Points[1]) <= HitTolerance + a.Thickness;

            case ToolType.Pen:
            case ToolType.Highlighter:
                for (var i = 0; i < a.Points.Count - 1; i++)
                    if (DistanceToSegment(p, a.Points[i], a.Points[i + 1]) <= HitTolerance + a.Thickness) return true;
                return false;

            default:
                var expanded = a.Bounds;
                expanded.Inflate(HitTolerance, HitTolerance);
                return expanded.Contains(p);
        }
    }

    private static double DistanceToSegment(Point p, Point a, Point b)
    {
        var ab = b - a;
        var len2 = ab.LengthSquared;
        if (len2 < 0.0001) return (p - a).Length;
        var t = Math.Clamp(((p - a).X * ab.X + (p - a).Y * ab.Y) / len2, 0, 1);
        var proj = a + ab * t;
        return (p - proj).Length;
    }

    // ---------- Mouse handling ----------

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        Focus();
        var p = e.GetPosition(this);
        _lastMouse = p;

        if (CurrentTool == ToolType.Select)
        {
            Logger.Log($"DrawingCanvasControl: Select-tool mouse down at {p}, clickCount={e.ClickCount}");
            if (e.ClickCount == 2)
            {
                var doubleClickHit = HitTest(p);
                Logger.Log($"DrawingCanvasControl: double-click at {p}, hit={doubleClickHit?.Type.ToString() ?? "null"}, locked={doubleClickHit?.Locked}");
                if (doubleClickHit is { Locked: false } && doubleClickHit.Type is ToolType.Text or ToolType.Rectangle or ToolType.Ellipse)
                {
                    _selected = doubleClickHit;
                    TextEditRequested?.Invoke(doubleClickHit);
                    return;
                }
            }

            if (_selected != null && !_selected.Locked)
            {
                if (_selected.Type is ToolType.Line or ToolType.Arrow)
                {
                    for (var i = 0; i < _selected.Points.Count; i++)
                    {
                        if ((p - _selected.Points[i]).Length <= HandleSize)
                        {
                            PushUndo();
                            _isDraggingPoint = true;
                            _draggingPointIndex = i;
                            CaptureMouse();
                            return;
                        }
                    }
                }
                else if ((p - new Point(_selected.Bounds.Right, _selected.Bounds.Bottom)).Length <= HandleSize
                         && _selected.Type is not ToolType.StepNumber)
                {
                    PushUndo();
                    _isResizing = true;
                    CaptureMouse();
                    return;
                }
            }

            var hit = HitTest(p);
            _selected = hit;
            if (hit != null)
            {
                PushUndo();
                _isMoving = true;
                CaptureMouse();
            }
            InvalidateVisual();
            return;
        }

        if (CurrentTool == ToolType.Crop)
        {
            _inProgress = new Annotation { Type = ToolType.Crop, Bounds = new Rect(p, new Size(0, 0)) };
            CaptureMouse();
            return;
        }

        if (CurrentTool == ToolType.Text)
        {
            Logger.Log($"DrawingCanvasControl: Text tool clicked at {p}, subscribers={TextToolClicked?.GetInvocationList().Length ?? 0}");
            TextToolClicked?.Invoke(p);
            return;
        }

        if (CurrentTool == ToolType.StepNumber)
        {
            PushUndo();
            const double r = 13;
            var stepAnnotation = new Annotation
            {
                Type = ToolType.StepNumber,
                Bounds = new Rect(p.X - r, p.Y - r, r * 2, r * 2),
                Color = StrokeColor,
                StepIndex = _nextStepNumber++
            };
            _annotations.Add(stepAnnotation);
            _selected = stepAnnotation;
            Raise();
            return;
        }

        _inProgress = new Annotation
        {
            Type = CurrentTool,
            Color = StrokeColor,
            Thickness = StrokeThickness,
            Bounds = new Rect(p, new Size(0, 0)),
            BackgroundColor = CurrentTool is ToolType.Rectangle or ToolType.Ellipse ? FillColor : Colors.Transparent
        };
        if (CurrentTool is ToolType.Pen or ToolType.Highlighter or ToolType.Line or ToolType.Arrow)
        {
            _inProgress.Points.Add(p);
            if (CurrentTool is ToolType.Line or ToolType.Arrow) _inProgress.Points.Add(p);
        }
        CaptureMouse();
    }

    /// <summary>Normalizes a drag from the fixed mouse-down anchor (<see cref="_lastMouse"/>) to
    /// <paramref name="current"/> into a non-negative Rect — used by both the Crop and the
    /// generic Rectangle/Ellipse/etc. drag-in-progress paths in <see cref="OnMouseMove"/>.
    /// Deliberately reads the anchor from <see cref="_lastMouse"/>, never from the in-progress
    /// annotation's own Bounds.TopLeft: that gets overwritten on every call to whatever the current
    /// min corner is, so reading it back next time would silently slide the anchor along with the
    /// shrinking edge — which is exactly what made dragging up/left broken while dragging down/right
    /// worked fine (TopLeft never changes when dragging down/right, so the bug stayed invisible).</summary>
    private Rect NormalizeDragBounds(Point current) => new(
        new Point(Math.Min(_lastMouse.X, current.X), Math.Min(_lastMouse.Y, current.Y)),
        new Point(Math.Max(_lastMouse.X, current.X), Math.Max(_lastMouse.Y, current.Y)));

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var p = e.GetPosition(this);

        if (_isDraggingPoint && _selected != null)
        {
            _selected.Points[_draggingPointIndex] = p;
            InvalidateVisual();
            return;
        }

        if (_isResizing && _selected != null)
        {
            var b = _selected.Bounds;
            _selected.Bounds = new Rect(b.X, b.Y, Math.Max(4, p.X - b.X), Math.Max(4, p.Y - b.Y));
            if (_selected.Type == ToolType.Blur) RecomputeBlur(_selected);
            InvalidateVisual();
            return;
        }

        if (_isMoving && _selected != null)
        {
            var delta = p - _lastMouse;
            _selected.Bounds = new Rect(_selected.Bounds.TopLeft + delta, _selected.Bounds.Size);
            _selected.Points = _selected.Points.Select(pt => pt + delta).ToList();
            _lastMouse = p;
            if (_selected.Type == ToolType.Blur) RecomputeBlur(_selected);
            InvalidateVisual();
            return;
        }

        if (_inProgress == null) return;

        if (_inProgress.Type == ToolType.Crop)
        {
            _inProgress.Bounds = NormalizeDragBounds(p);
            InvalidateVisual();
            return;
        }

        if (_inProgress.Type is ToolType.Pen or ToolType.Highlighter)
        {
            _inProgress.Points.Add(p);
        }
        else if (_inProgress.Type is ToolType.Line or ToolType.Arrow)
        {
            _inProgress.Points[1] = p;
        }
        else
        {
            _inProgress.Bounds = NormalizeDragBounds(p);
        }
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_isDraggingPoint || _isResizing || _isMoving)
        {
            _isDraggingPoint = false;
            _isResizing = false;
            _isMoving = false;
            ReleaseMouseCapture();
            Raise();
            return;
        }

        if (_inProgress == null) return;
        ReleaseMouseCapture();

        if (_inProgress.Type == ToolType.Crop)
        {
            var rect = _inProgress.Bounds;
            rect.Intersect(new Rect(0, 0, ActualWidth, ActualHeight));
            _pendingCrop = rect.Width > 4 && rect.Height > 4 ? rect : null;
            _inProgress = null;
            CropProposed?.Invoke(_pendingCrop);
            InvalidateVisual();
            return;
        }

        var finished = _inProgress;
        _inProgress = null;

        var tooSmall = finished.Type is ToolType.Rectangle or ToolType.Ellipse or ToolType.Blur
                       && (finished.Bounds.Width < 3 || finished.Bounds.Height < 3);
        var tooShort = finished.Type is ToolType.Line or ToolType.Arrow
                       && finished.Points.Count >= 2 && (finished.Points[0] - finished.Points[1]).Length < 3;
        var tooFewPoints = finished.Type is ToolType.Pen or ToolType.Highlighter && finished.Points.Count < 2;

        if (tooSmall || tooShort || tooFewPoints)
        {
            InvalidateVisual();
            return;
        }

        if (finished.Type == ToolType.Blur) RecomputeBlur(finished);

        PushUndo();
        _annotations.Add(finished);
        _selected = finished;
        Raise();
    }

    private void RecomputeBlur(Annotation a)
    {
        var rect = a.Bounds;
        rect.Intersect(new Rect(0, 0, _background.PixelWidth, _background.PixelHeight));
        if (rect.Width < 2 || rect.Height < 2) { a.BlurTile = null; return; }

        var px = new Int32Rect((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height);
        var blockSize = Math.Max(4, (int)(a.Thickness * 2));
        a.BlurTile = Pixelate(_background, px, blockSize);
    }

    private static WriteableBitmap Pixelate(BitmapSource source, Int32Rect region, int blockSize)
    {
        var cropped = new CroppedBitmap(source, region);
        var converted = new FormatConvertedBitmap(cropped, PixelFormats.Bgra32, null, 0);

        var w = region.Width;
        var h = region.Height;
        var stride = w * 4;
        var pixels = new byte[h * stride];
        converted.CopyPixels(pixels, stride, 0);

        for (var by = 0; by < h; by += blockSize)
        {
            var bh = Math.Min(blockSize, h - by);
            for (var bx = 0; bx < w; bx += blockSize)
            {
                var bw = Math.Min(blockSize, w - bx);
                long a = 0, r = 0, g = 0, b = 0;
                var count = 0;
                for (var y = 0; y < bh; y++)
                {
                    for (var x = 0; x < bw; x++)
                    {
                        var idx = ((by + y) * w + (bx + x)) * 4;
                        b += pixels[idx]; g += pixels[idx + 1]; r += pixels[idx + 2]; a += pixels[idx + 3];
                        count++;
                    }
                }
                if (count == 0) continue;
                var avgB = (byte)(b / count); var avgG = (byte)(g / count);
                var avgR = (byte)(r / count); var avgA = (byte)(a / count);
                for (var y = 0; y < bh; y++)
                {
                    for (var x = 0; x < bw; x++)
                    {
                        var idx = ((by + y) * w + (bx + x)) * 4;
                        pixels[idx] = avgB; pixels[idx + 1] = avgG; pixels[idx + 2] = avgR; pixels[idx + 3] = avgA;
                    }
                }
            }
        }

        var wb = new WriteableBitmap(w, h, 96, 96, PixelFormats.Bgra32, null);
        wb.WritePixels(new Int32Rect(0, 0, w, h), pixels, stride, 0);
        wb.Freeze();
        return wb;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if ((e.Key == Key.Delete || e.Key == Key.Back) && _selected != null)
        {
            PushUndo();
            _annotations.Remove(_selected);
            _selected = null;
            Raise();
        }
        base.OnKeyDown(e);
    }
}
