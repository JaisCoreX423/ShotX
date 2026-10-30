using System.Windows;
using System.Windows.Media;
using Point = System.Windows.Point;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Pen = System.Windows.Media.Pen;

namespace ShotX.Controls;

/// <summary>
/// Renders the Greenshot-style capture overlay: a dim scrim with a cut-out "hole" over the
/// current selection (or window-snap suggestion), an accent border around it, and crosshair
/// guide lines that follow the cursor before a drag starts.
/// </summary>
public class SelectionOverlayCanvas : FrameworkElement
{
    public static readonly DependencyProperty ScrimBrushProperty =
        DependencyProperty.Register(nameof(ScrimBrush), typeof(Brush), typeof(SelectionOverlayCanvas),
            new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AccentBrushProperty =
        DependencyProperty.Register(nameof(AccentBrush), typeof(Brush), typeof(SelectionOverlayCanvas),
            new FrameworkPropertyMetadata(Brushes.DeepSkyBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush ScrimBrush
    {
        get => (Brush)GetValue(ScrimBrushProperty);
        set => SetValue(ScrimBrushProperty, value);
    }

    public Brush AccentBrush
    {
        get => (Brush)GetValue(AccentBrushProperty);
        set => SetValue(AccentBrushProperty, value);
    }

    private Rect? _hole;
    private Point? _crosshair;
    private bool _holeIsSuggestion;

    public void SetHole(Rect? hole, bool isSuggestion)
    {
        _hole = hole;
        _holeIsSuggestion = isSuggestion;
        InvalidateVisual();
    }

    public void SetCrosshair(Point? point)
    {
        _crosshair = point;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);

        if (_hole is { } hole && hole.Width > 0 && hole.Height > 0)
        {
            var geometry = new GeometryGroup { FillRule = FillRule.EvenOdd };
            geometry.Children.Add(new RectangleGeometry(bounds));
            geometry.Children.Add(new RectangleGeometry(hole));
            dc.DrawGeometry(ScrimBrush, null, geometry);

            var pen = new Pen(AccentBrush, _holeIsSuggestion ? 1.5 : 2)
            {
                DashStyle = _holeIsSuggestion ? new DashStyle(new double[] { 4, 3 }, 0) : new DashStyle()
            };
            dc.DrawRectangle(null, pen, hole);
        }
        else
        {
            dc.DrawRectangle(ScrimBrush, null, bounds);
        }

        if (_crosshair is { } p && _hole is null)
        {
            var pen = new Pen(AccentBrush, 1) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) };
            dc.DrawLine(pen, new Point(p.X, 0), new Point(p.X, ActualHeight));
            dc.DrawLine(pen, new Point(0, p.Y), new Point(ActualWidth, p.Y));
        }
    }
}
