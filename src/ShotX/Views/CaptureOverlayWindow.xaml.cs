using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using ShotX.Services;
using Point = System.Windows.Point;
using Rect = System.Windows.Rect;
using Rectangle = System.Drawing.Rectangle;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace ShotX.Views;

/// <summary>
/// Full-virtual-desktop overlay for picking a capture region: drag to select a custom
/// rectangle, or click once on a window to snap to its bounds. Esc cancels.
/// </summary>
public partial class CaptureOverlayWindow : Window
{
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    private static readonly IntPtr HwndTopmost = new(-1);
    private const uint SwpNoActivate = 0x0010;

    private readonly System.Drawing.Bitmap _fullScreenshot;
    private readonly Rectangle _virtualBoundsPx;
    private readonly double _scale;
    private readonly BitmapSource _fullScreenshotSource;
    private readonly TaskCompletionSource<Rectangle?> _completion = new();

    private bool _isDragging;
    private Point _dragAnchorDip;
    private Rectangle? _currentSuggestionPx;

    public CaptureOverlayWindow(System.Drawing.Bitmap fullScreenshot, Rectangle virtualBoundsPx, double scale)
    {
        InitializeComponent();
        _fullScreenshot = fullScreenshot;
        _virtualBoundsPx = virtualBoundsPx;
        _scale = scale;
        _fullScreenshotSource = ScreenCaptureService.ToBitmapSource(fullScreenshot);

        Left = virtualBoundsPx.X / scale;
        Top = virtualBoundsPx.Y / scale;
        Width = virtualBoundsPx.Width / scale;
        Height = virtualBoundsPx.Height / scale;

        BackgroundImage.Source = _fullScreenshotSource;
        HintText.Text = Loc.T("overlay.hint");
        Overlay.ScrimBrush = (System.Windows.Media.Brush)FindResource("Brush.ScrimDim");
        Overlay.AccentBrush = (System.Windows.Media.Brush)FindResource("Brush.Accent");

        SourceInitialized += OnSourceInitialized;
        Loaded += (_, _) => Keyboard.Focus(this);
    }

    public Task<Rectangle?> SelectionTask => _completion.Task;

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        SetWindowPos(hwnd, HwndTopmost, _virtualBoundsPx.X, _virtualBoundsPx.Y,
            _virtualBoundsPx.Width, _virtualBoundsPx.Height, SwpNoActivate);
    }

    private Rectangle DipRectToPx(Rect dip)
    {
        return new Rectangle(
            _virtualBoundsPx.X + (int)Math.Round(dip.X * _scale),
            _virtualBoundsPx.Y + (int)Math.Round(dip.Y * _scale),
            (int)Math.Round(dip.Width * _scale),
            (int)Math.Round(dip.Height * _scale));
    }

    private Rect PxRectToDip(Rectangle px)
    {
        return new Rect(
            (px.X - _virtualBoundsPx.X) / _scale,
            (px.Y - _virtualBoundsPx.Y) / _scale,
            px.Width / _scale,
            px.Height / _scale);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var posDip = e.GetPosition(RootGrid);

        if (_isDragging)
        {
            var rectDip = new Rect(
                Math.Min(_dragAnchorDip.X, posDip.X), Math.Min(_dragAnchorDip.Y, posDip.Y),
                Math.Abs(posDip.X - _dragAnchorDip.X), Math.Abs(posDip.Y - _dragAnchorDip.Y));
            Overlay.SetHole(rectDip, isSuggestion: false);
            Overlay.SetCrosshair(null);
            var px = DipRectToPx(rectDip);
            ShowDimensionBadge(posDip, px.Width, px.Height);
        }
        else
        {
            var screenPx = new System.Drawing.Point(
                _virtualBoundsPx.X + (int)Math.Round(posDip.X * _scale),
                _virtualBoundsPx.Y + (int)Math.Round(posDip.Y * _scale));
            _currentSuggestionPx = ScreenCaptureService.GetWindowBoundsAtPoint(screenPx);

            if (_currentSuggestionPx is { } suggestion)
            {
                var rectDip = PxRectToDip(suggestion);
                Overlay.SetHole(rectDip, isSuggestion: true);
                Overlay.SetCrosshair(null);
                ShowDimensionBadge(posDip, suggestion.Width, suggestion.Height);
            }
            else
            {
                Overlay.SetHole(null, isSuggestion: false);
                Overlay.SetCrosshair(posDip);
                HintBadge.Visibility = Visibility.Visible;
                DimensionBadge.Visibility = Visibility.Collapsed;
            }
        }

        UpdateMagnifier(posDip);
        base.OnMouseMove(e);
    }

    private void ShowDimensionBadge(Point anchorDip, int widthPx, int heightPx)
    {
        HintBadge.Visibility = Visibility.Collapsed;
        DimensionBadge.Visibility = Visibility.Visible;
        DimensionText.Text = $"{widthPx} × {heightPx}";
        DimensionBadge.Margin = new Thickness(anchorDip.X + 14, anchorDip.Y + 18, 0, 0);
        DimensionBadge.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        DimensionBadge.VerticalAlignment = System.Windows.VerticalAlignment.Top;
    }

    private void UpdateMagnifier(Point posDip)
    {
        const int sampleSize = 24; // source px sampled, shown at ~5x in a 120x120 circle
        var centerPx = new System.Drawing.Point(
            (int)Math.Round(posDip.X * _scale) - _virtualBoundsPx.X,
            (int)Math.Round(posDip.Y * _scale) - _virtualBoundsPx.Y);

        var half = sampleSize / 2;
        var cropX = Math.Clamp(centerPx.X - half, 0, Math.Max(0, _fullScreenshot.Width - sampleSize));
        var cropY = Math.Clamp(centerPx.Y - half, 0, Math.Max(0, _fullScreenshot.Height - sampleSize));
        var cropRect = new Int32Rect(cropX, cropY, Math.Min(sampleSize, _fullScreenshot.Width),
            Math.Min(sampleSize, _fullScreenshot.Height));

        try
        {
            var cropped = new CroppedBitmap(_fullScreenshotSource, cropRect);
            MagnifierImage.Source = cropped;
            MagnifierImage.Width = sampleSize * 5;
            MagnifierImage.Height = sampleSize * 5;
        }
        catch
        {
            // Near an edge; just skip this frame's magnifier update.
        }

        MagnifierBorder.Visibility = Visibility.Visible;
        var offsetX = posDip.X + 24;
        var offsetY = posDip.Y + 24;
        if (offsetX + 120 > ActualWidth) offsetX = posDip.X - 144;
        if (offsetY + 120 > ActualHeight) offsetY = posDip.Y - 144;
        MagnifierBorder.Margin = new Thickness(offsetX, offsetY, 0, 0);
        MagnifierBorder.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        MagnifierBorder.VerticalAlignment = System.Windows.VerticalAlignment.Top;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        _isDragging = true;
        _dragAnchorDip = e.GetPosition(RootGrid);
        CaptureMouse();
        base.OnMouseLeftButtonDown(e);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        if (_isDragging)
        {
            _isDragging = false;
            ReleaseMouseCapture();

            var posDip = e.GetPosition(RootGrid);
            var dragDistance = (posDip - _dragAnchorDip).Length;

            if (dragDistance > 4)
            {
                var rectDip = new Rect(
                    Math.Min(_dragAnchorDip.X, posDip.X), Math.Min(_dragAnchorDip.Y, posDip.Y),
                    Math.Abs(posDip.X - _dragAnchorDip.X), Math.Abs(posDip.Y - _dragAnchorDip.Y));
                Finish(DipRectToPx(rectDip));
            }
            else if (_currentSuggestionPx is { } suggestion)
            {
                Finish(suggestion);
            }
        }
        base.OnMouseLeftButtonUp(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Finish(null);
        }
        base.OnKeyDown(e);
    }

    private void Finish(Rectangle? resultPx)
    {
        _completion.TrySetResult(resultPx);
        Close();
    }
}
