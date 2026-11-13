using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using ShotX.Controls;
using ShotX.Models;
using ShotX.Services;
using Color = System.Windows.Media.Color;
using ColorConverter = System.Windows.Media.ColorConverter;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using Button = System.Windows.Controls.Button;
using RadioButton = System.Windows.Controls.RadioButton;
using ComboBox = System.Windows.Controls.ComboBox;
using Orientation = System.Windows.Controls.Orientation;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace ShotX.Views;

public partial class EditorWindow : Window
{
    private static readonly (string Label, Color Color)[] Palette =
    {
        ("Rojo", Color.FromRgb(0xFF, 0x3B, 0x30)),
        ("Naranja", Color.FromRgb(0xFF, 0x95, 0x00)),
        ("Amarillo", Color.FromRgb(0xFF, 0xCC, 0x00)),
        ("Verde", Color.FromRgb(0x34, 0xC7, 0x59)),
        ("Teal", Color.FromRgb(0x30, 0xB0, 0xC7)),
        ("Azul", Color.FromRgb(0x0A, 0x84, 0xFF)),
        ("Morado", Color.FromRgb(0xAF, 0x52, 0xDE)),
        ("Rosa", Color.FromRgb(0xFF, 0x37, 0x5F)),
        ("Negro", Color.FromRgb(0x1D, 0x1D, 0x1F)),
        ("Blanco", Colors.White),
    };

    private static readonly (ToolType Tool, string Glyph, string TipKey)[] Tools =
    {
        (ToolType.Select, "↖", "tool.select"),
        (ToolType.Rectangle, "▭", "tool.rectangle"),
        (ToolType.Ellipse, "◯", "tool.ellipse"),
        (ToolType.Line, "╱", "tool.line"),
        (ToolType.Arrow, "→", "tool.arrow"),
        (ToolType.Pen, "✎", "tool.pen"),
        (ToolType.Highlighter, "█", "tool.highlighter"),
        (ToolType.Text, "T", "tool.text"),
        (ToolType.StepNumber, "①", "tool.stepNumber"),
        (ToolType.Blur, "▒", "tool.blur"),
        (ToolType.Crop, "✂", "tool.crop"),
    };

    private static readonly Dictionary<ToolType, string> LayerTypeKeys = new()
    {
        [ToolType.Rectangle] = "layers.type.rectangle",
        [ToolType.Ellipse] = "layers.type.ellipse",
        [ToolType.Line] = "layers.type.line",
        [ToolType.Arrow] = "layers.type.arrow",
        [ToolType.Pen] = "layers.type.pen",
        [ToolType.Highlighter] = "layers.type.highlighter",
        [ToolType.Text] = "layers.type.text",
        [ToolType.StepNumber] = "layers.type.stepNumber",
        [ToolType.Blur] = "layers.type.blur",
        [ToolType.Image] = "layers.type.image",
    };

    /// <summary>Raised when the toolbar's gear icon is clicked, so whoever owns the shared
    /// Settings window (App.xaml.cs) can open it without the editor needing a direct reference.</summary>
    public event Action? SettingsRequested;

    private readonly DrawingCanvasControl _canvas;
    private readonly Dictionary<ToolType, ToggleButton> _toolButtons = new();
    private Button _undoButton = null!;
    private Button _redoButton = null!;
    private Color _currentColor;
    private int _currentFontSize;
    private Color _currentTextBackground;
    private Color _bgBaseColor;
    private Slider _bgOpacitySlider = null!;
    private ToggleButton _bgToggleButton = null!;
    private Border _bgPreviewSwatch = null!;
    private TextHAlign _currentTextHAlign;
    private TextVAlign _currentTextVAlign;
    private ToggleButton _alignToggleButton = null!;
    private readonly Dictionary<TextHAlign, RadioButton> _hAlignButtons = new();
    private readonly Dictionary<TextVAlign, RadioButton> _vAlignButtons = new();
    private string? _savedFilePath;
    private bool _hasUnsavedChanges;

    public EditorWindow(BitmapSource image, string? initialStatus = null)
    {
        InitializeComponent();
        MaximizeBoundsFix.Apply(this);
        WindowState = WindowState.Maximized;

        // Remembered from the last screenshot's choices, so picking a color/size/fill carries over.
        var remembered = SettingsService.Current;
        _currentColor = (Color)ColorConverter.ConvertFromString(remembered.LastColor);
        _currentFontSize = (int)Math.Round(remembered.LastFontSize);
        _bgBaseColor = (Color)ColorConverter.ConvertFromString(remembered.LastFillBaseColor);
        var rememberedAlpha = (byte)Math.Round(remembered.LastFillOpacity / 100.0 * 255);
        _currentTextBackground = Color.FromArgb(rememberedAlpha, _bgBaseColor.R, _bgBaseColor.G, _bgBaseColor.B);
        _currentTextHAlign = Enum.TryParse<TextHAlign>(remembered.LastTextHAlign, out var h) ? h : TextHAlign.Center;
        _currentTextVAlign = Enum.TryParse<TextVAlign>(remembered.LastTextVAlign, out var v) ? v : TextVAlign.Center;

        _canvas = new DrawingCanvasControl(image);
        _canvas.StrokeColor = _currentColor;
        _canvas.StrokeThickness = remembered.LastThickness;
        _canvas.FillColor = _currentTextBackground;
        // Insert at the bottom of CanvasContainer's z-order — TextInputBox/CropConfirmPanel are
        // declared ahead of it in XAML (so InitializeComponent adds them first) and must stay
        // visually on top of the canvas they overlay.
        CanvasContainer.Children.Insert(0, _canvas);

        _canvas.Changed += (_, _) =>
        {
            _hasUnsavedChanges = true;
            UpdateUndoRedoState();
            RefreshLayersPanel();
            RefreshSizeStepperDisplay();
        };
        _canvas.TextToolClicked += OnCanvasTextToolClicked;
        _canvas.CropProposed += OnCropProposed;
        _canvas.TextEditRequested += OnTextEditRequested;

        _sizeStepperValue = (int)Math.Round(remembered.LastThickness);
        BuildToolbar();
        SelectTool(ToolType.Select);
        UpdateUndoRedoState();
        ApplyLocalizedLabels();
        RefreshLayersPanel();

        StatusText.Text = initialStatus ?? $"{image.PixelWidth} × {image.PixelHeight}";

        StateChanged += (_, _) => UpdateMaximizeGlyph();

        Loaded += (_, _) =>
        {
            _canvas.Focus();
            PlayOpenAnimation();
        };
    }

    private void ApplyLocalizedLabels()
    {
        CopyBarButton.Content = Loc.T("editor.copy");
        SaveAsBarButton.Content = Loc.T("editor.saveAs");
        SaveButton.Content = Loc.T("editor.save");
        ApplyCropButton.Content = Loc.T("editor.applyCrop");
        CancelCropButton.Content = Loc.T("editor.cancel");
        LayersTitleText.Text = Loc.T("layers.title");
        MinimizeButton.ToolTip = Loc.T("window.minimize");
        MaximizeButton.ToolTip = WindowState == WindowState.Maximized ? Loc.T("window.restore") : Loc.T("window.maximize");
        CloseButton.ToolTip = Loc.T("window.close");
    }

    private void UpdateMaximizeGlyph()
    {
        var isMaximized = WindowState == WindowState.Maximized;
        MaximizeButton.Content = isMaximized ? "" : "";
        MaximizeButton.ToolTip = isMaximized ? Loc.T("window.restore") : Loc.T("window.maximize");
    }

    private void OnMaximizeClick(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    public void SimulateTextToolClickForTesting(Point location) => OnCanvasTextToolClicked(location);

    public void SimulateTextToolSelectForTesting()
    {
        SelectTool(ToolType.Text);
        ShowCenteredTextBox();
    }

    public void SimulateTextCommitAndResizeForTesting(string text)
    {
        TextInputBox.Text = text;
        CommitTextInput();
        if (_canvas.Selected is { } annotation)
        {
            // Resizing no longer touches FontSize — it stays exactly as typed, same as a shape's label.
            annotation.Bounds = new Rect(annotation.Bounds.X, annotation.Bounds.Y, annotation.Bounds.Width + 120, annotation.Bounds.Height + 60);
        }
    }

    public void SimulateSelectionStepperForTesting()
    {
        var rect = _canvas.Annotations.FirstOrDefault(a => a.Type == ToolType.Rectangle);
        if (rect != null)
        {
            _canvas.SelectAnnotationExternally(rect);
            _canvas.ApplyThicknessToSelection(18);
        }
    }

    public FrameworkElement? BackgroundPopupContentForTesting { get; private set; }

    public void SimulateOpenBackgroundPopupForTesting() => _bgToggleButton.IsChecked = true;

    public void SimulateSetDrawingDefaultsForTesting()
    {
        // Simulates the user picking a color/size/fill, as if via the toolbar controls.
        _currentColor = Color.FromRgb(0x30, 0xB0, 0xC7); // teal
        _canvas.StrokeColor = _currentColor;
        SettingsService.Current.LastColor = _currentColor.ToString();

        _sizeStepperValue = 12;
        _canvas.StrokeThickness = _sizeStepperValue;
        SettingsService.Current.LastThickness = _sizeStepperValue;

        _bgBaseColor = Color.FromRgb(0xAF, 0x52, 0xDE); // purple
        _bgOpacitySlider.Value = 70;
        SettingsService.Current.LastFillBaseColor = _bgBaseColor.ToString();
        SettingsService.Current.LastFillOpacity = 70;
        SettingsService.Save();
    }

    public void SimulateDrawRectangleForTesting() => _canvas.AddRectangleWithCurrentDefaultsForTesting();

    public void SimulateCropDragInProgressForTesting() => _canvas.SimulateCropDragInProgressForTesting(new Rect(120, 100, 400, 260));

    /// <summary>Drags a Rectangle from (400,350) up-and-left to (150,150) in several steps —
    /// the exact direction that used to break (dragging down-right never exposed the bug).</summary>
    public void SimulateLeftDragRectangleForTesting()
    {
        _canvas.SimulateMultiStepShapeDragForTesting(ToolType.Rectangle, new Point(400, 350),
            new Point(390, 340), new Point(300, 260), new Point(200, 180), new Point(150, 150));
        _canvas.FinishMultiStepShapeDragForTesting();
    }

    public void SimulateColorApplyToSelectionForTesting()
    {
        var rect = _canvas.Annotations.FirstOrDefault(a => a.Type == ToolType.Rectangle);
        if (rect != null)
        {
            _canvas.SelectAnnotationExternally(rect);
            _canvas.ApplyColorToSelection(Color.FromRgb(0x34, 0xC7, 0x59)); // green
        }
    }

    public void SimulateTextBackgroundForTesting()
    {
        var text = _canvas.Annotations.FirstOrDefault(a => a.Type == ToolType.Text);
        if (text != null)
        {
            _canvas.SelectAnnotationExternally(text);
            _canvas.ApplyFillToSelection(Color.FromArgb(180, 0xFF, 0xCC, 0x00)); // translucent yellow
        }
    }

    public void SimulateLayerReorderForTesting()
    {
        var rect = _canvas.Annotations.FirstOrDefault(a => a.Type == ToolType.Rectangle);
        var ellipse = _canvas.Annotations.FirstOrDefault(a => a.Type == ToolType.Ellipse);
        if (rect != null && ellipse != null) _canvas.ReorderAnnotation(rect, ellipse);
    }

    public void SimulateTextSizeStepperForTesting()
    {
        var text = _canvas.Annotations.FirstOrDefault(a => a.Type == ToolType.Text);
        if (text == null) return;
        _canvas.SelectAnnotationExternally(text);
        RefreshSizeStepperDisplay();
        for (var i = 0; i < 10; i++) AdjustSizeStepper(1);
    }

    public void SimulateDoubleClickEditForTesting()
    {
        var textAnnotation = _canvas.Annotations.LastOrDefault(a => a.Type == ToolType.Text);
        if (textAnnotation != null) OnTextEditRequested(textAnnotation);
    }

    public void SimulateImageImportForTesting(BitmapSource image)
    {
        _canvas.AddImageAnnotation(image);
        RefreshLayersPanel();
    }

    /// <summary>Double-clicks a Rectangle (or Ellipse, if none) to open it for label editing, types
    /// text, and commits — exercising the "write inside a shape" feature end-to-end.</summary>
    public void SimulateShapeTextEditForTesting(string text)
    {
        var shape = _canvas.Annotations.FirstOrDefault(a => a.Type == ToolType.Rectangle)
                    ?? _canvas.Annotations.FirstOrDefault(a => a.Type == ToolType.Ellipse);
        if (shape == null) return;
        OnTextEditRequested(shape);
        TextInputBox.Text = text;
        CommitTextInput();
    }

    /// <summary>Opens a rectangle's label editor and leaves it open (no commit) — for snapshotting
    /// the box itself mid-edit, e.g. to confirm its background doesn't hide the shape underneath.</summary>
    public void SimulateOpenShapeLabelEditForTesting()
    {
        var rect = _canvas.Annotations.FirstOrDefault(a => a.Type == ToolType.Rectangle);
        if (rect == null) return;
        // Give it a fill first — an opaque edit-box background would be obvious against it.
        _canvas.SelectAnnotationExternally(rect);
        _canvas.ApplyFillToSelection(Color.FromRgb(0xFF, 0xCC, 0x00));
        OnTextEditRequested(rect);
        TextInputBox.Text = "Dentro";
    }

    /// <summary>Gives a rectangle a label, then sets the label's color to something different from
    /// its (unchanged) outline color — proving the two are independently editable.</summary>
    public void SimulateShapeLabelColorForTesting(Color labelColor)
    {
        var rect = _canvas.Annotations.FirstOrDefault(a => a.Type == ToolType.Rectangle);
        if (rect == null) return;
        OnTextEditRequested(rect);
        TextInputBox.Text = "Etiqueta";
        CommitTextInput();
        _canvas.SelectAnnotationExternally(rect);
        _canvas.ApplyLabelColorToSelection(labelColor);
    }

    public void SimulateTextAlignForTesting(TextHAlign hAlign, TextVAlign vAlign)
    {
        var shape = _canvas.Annotations.FirstOrDefault(a => a.Type == ToolType.Rectangle && !string.IsNullOrEmpty(a.Text))
                    ?? _canvas.Annotations.FirstOrDefault(a => a.Type == ToolType.Text);
        if (shape == null) return;
        _canvas.SelectAnnotationExternally(shape);
        _canvas.ApplyTextAlignToSelection(hAlign, vAlign);
    }

    public void SimulateDuplicateShortcutForTesting()
    {
        var rect = _canvas.Annotations.FirstOrDefault(a => a.Type == ToolType.Rectangle);
        if (rect == null) return;
        _canvas.SelectAnnotationExternally(rect);
        _canvas.DuplicateSelected();
    }

    public void SimulateCopyPasteShortcutForTesting()
    {
        var rect = _canvas.Annotations.FirstOrDefault(a => a.Type == ToolType.Rectangle);
        if (rect == null) return;
        _canvas.SelectAnnotationExternally(rect);
        _canvas.CopySelectedToClipboard();
        _canvas.PasteFromClipboard();
    }

    public void SimulateDeleteSelectedForTesting()
    {
        var ellipse = _canvas.Annotations.FirstOrDefault(a => a.Type == ToolType.Ellipse);
        if (ellipse == null) return;
        _canvas.SelectAnnotationExternally(ellipse);
        _canvas.DeleteAnnotationExternally(ellipse);
    }

    /// <summary>Selects a shape and simulates the first keystroke typed into it, exercising the
    /// real <see cref="OnPreviewTextInput"/> routed-event path (not calling the edit logic
    /// directly). Only the triggering keystroke is simulated this way — once the box has real
    /// keyboard focus, further characters go through ordinary TextBox input, which isn't specific
    /// to this feature and needs no separate harness.</summary>
    public void SimulateTypeToEditShapeForTesting(string firstChar)
    {
        // Deliberately does NOT switch to the Select tool first — a freshly drawn shape stays
        // selected while its drawing tool (Rectangle/Ellipse) is still the active one, which is
        // exactly the real scenario the Select-tool-only check used to miss.
        var rect = _canvas.Annotations.FirstOrDefault(a => a.Type == ToolType.Rectangle);
        if (rect == null) return;
        _canvas.CurrentTool = ToolType.Rectangle;
        _canvas.SelectAnnotationExternally(rect);

        var composition = new TextComposition(InputManager.Current, this, firstChar);
        var e = new TextCompositionEventArgs(InputManager.Current.PrimaryKeyboardDevice, composition)
        {
            RoutedEvent = TextCompositionManager.PreviewTextInputEvent
        };
        RaiseEvent(e);
    }

    public void AddSampleAnnotationsForTesting()
    {
        _canvas.AddSampleAnnotationsForTesting();
        UpdateUndoRedoState();
        RefreshLayersPanel();
    }

    public static EditorWindow ForNewCapture(BitmapSource image)
    {
        return new EditorWindow(image, $"{image.PixelWidth} × {image.PixelHeight} · {Loc.T("editor.unsaved")}");
    }

    // ---------- MCP ----------

    /// <summary>The annotation surface, for the MCP server to add/read annotations on directly.</summary>
    public DrawingCanvasControl Canvas => _canvas;

    /// <summary>Saves (same flattening as Save/Save As) to an explicit path, or — when none is
    /// given — the same default the toolbar's Save button would use. Returns the path actually used.</summary>
    public string SaveForMcp(string? path = null)
    {
        var settings = SettingsService.Current;
        Directory.CreateDirectory(settings.SaveFolder);
        var target = path ?? _savedFilePath ?? Path.Combine(settings.SaveFolder, $"ShotX_{DateTime.Now:yyyyMMdd_HHmmss}.png");
        SaveToFile(target);
        _savedFilePath = target;
        _hasUnsavedChanges = false;
        StatusText.Text = string.Format(Loc.T("editor.savedAt"), target);
        return target;
    }

    private void PlayOpenAnimation()
    {
        Opacity = 0;
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        var scaleX = new DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        var scaleY = new DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(260)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        BeginAnimation(OpacityProperty, fade);
        WindowScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
        WindowScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
    }

    // ---------- Toolbar ----------

    private void BuildToolbar()
    {
        foreach (var (tool, glyph, tipKey) in Tools)
        {
            var button = new ToggleButton
            {
                Style = (Style)FindResource("ToolbarButtonStyle"),
                Content = new TextBlock { Text = glyph, FontSize = 16, HorizontalAlignment = System.Windows.HorizontalAlignment.Center },
                ToolTip = Loc.T(tipKey),
                Tag = tool
            };
            button.Click += (_, _) =>
            {
                SelectTool(tool);
                if (tool == ToolType.Text) ShowCenteredTextBox();
            };
            _toolButtons[tool] = button;
            ToolbarPanel.Children.Add(button);
        }

        var importButton = new Button
        {
            Style = (Style)FindResource("IconButtonStyle"),
            Width = 36,
            Height = 36,
            Content = new TextBlock { Text = "◱", FontSize = 16, HorizontalAlignment = System.Windows.HorizontalAlignment.Center },
            ToolTip = Loc.T("tool.importImage")
        };
        importButton.Click += OnImportImageClick;
        ToolbarPanel.Children.Add(importButton);

        ToolbarPanel.Children.Add(new System.Windows.Shapes.Rectangle { Style = (Style)FindResource("SectionDividerStyle") });

        _undoButton = new Button
        {
            Style = (Style)FindResource("IconButtonStyle"),
            Content = new TextBlock { Text = "↶", FontSize = 17 },
            ToolTip = Loc.T("tool.undo")
        };
        _undoButton.Click += (_, _) => { _canvas.Undo(); UpdateUndoRedoState(); RefreshLayersPanel(); };

        _redoButton = new Button
        {
            Style = (Style)FindResource("IconButtonStyle"),
            Content = new TextBlock { Text = "↷", FontSize = 17 },
            ToolTip = Loc.T("tool.redo")
        };
        _redoButton.Click += (_, _) => { _canvas.Redo(); UpdateUndoRedoState(); RefreshLayersPanel(); };

        ToolbarPanel.Children.Add(_undoButton);
        ToolbarPanel.Children.Add(_redoButton);

        ToolbarPanel.Children.Add(new System.Windows.Shapes.Rectangle { Style = (Style)FindResource("SectionDividerStyle") });

        var swatchWrap = new WrapPanel { Width = 140, VerticalAlignment = System.Windows.VerticalAlignment.Center };
        foreach (var (label, color) in Palette)
        {
            var swatch = new RadioButton
            {
                Style = (Style)FindResource("SwatchButtonStyle"),
                GroupName = "ColorSwatches",
                Tag = new SolidColorBrush(color),
                ToolTip = label,
                IsChecked = color == _currentColor
            };
            swatch.Checked += (_, _) =>
            {
                _currentColor = color;
                _canvas.StrokeColor = color;
                if (TextInputBox.Visibility == Visibility.Visible) TextInputBox.Foreground = new SolidColorBrush(color);

                // While editing a shape's label, color changes the label text, not the shape's own
                // outline — the two are independent, unlike a standalone Text layer, whose outline
                // and text color are the same thing.
                if (_editingTextAnnotation is { Type: ToolType.Rectangle or ToolType.Ellipse })
                    _canvas.ApplyLabelColorToSelection(color);
                else
                    _canvas.ApplyColorToSelection(color);

                SettingsService.Current.LastColor = color.ToString();
                SettingsService.Save();
            };
            swatchWrap.Children.Add(swatch);
        }
        ToolbarPanel.Children.Add(swatchWrap);

        ToolbarPanel.Children.Add(new System.Windows.Shapes.Rectangle { Style = (Style)FindResource("SectionDividerStyle") });

        ToolbarPanel.Children.Add(BuildSizeStepper());

        ToolbarPanel.Children.Add(new System.Windows.Shapes.Rectangle { Style = (Style)FindResource("SectionDividerStyle") });

        ToolbarPanel.Children.Add(BuildBackgroundControl());

        ToolbarPanel.Children.Add(BuildTextAlignControl());

        ToolbarPanel.Children.Add(new System.Windows.Shapes.Rectangle { Style = (Style)FindResource("SectionDividerStyle") });

        var settingsButton = new Button
        {
            Style = (Style)FindResource("IconButtonStyle"),
            Content = new TextBlock { Text = "⚙", FontSize = 17 },
            ToolTip = Loc.T("main.settings")
        };
        settingsButton.Click += (_, _) => SettingsRequested?.Invoke();
        ToolbarPanel.Children.Add(settingsButton);
    }

    private static readonly ToolType[] ThicknessTypes =
        { ToolType.Rectangle, ToolType.Ellipse, ToolType.Line, ToolType.Arrow, ToolType.Pen, ToolType.Highlighter, ToolType.Blur };

    private static readonly ToolType[] FillableAnnotationTypes = { ToolType.Text, ToolType.Rectangle, ToolType.Ellipse };

    private int _sizeStepperValue = 4;
    private TextBlock _sizeStepperValueText = null!;

    /// <summary>One size control that adapts to context: font size for a selected (or about-to-be-
    /// created) text layer, stroke width for everything else — instead of two separately-labeled,
    /// easy-to-confuse steppers.</summary>
    private FrameworkElement BuildSizeStepper()
    {
        _sizeStepperValueText = new TextBlock
        {
            Text = _sizeStepperValue.ToString(),
            FontSize = 13,
            Width = 24,
            TextAlignment = TextAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        };
        _sizeStepperValueText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextPrimary");

        var minus = new Button { Content = "−", Width = 26, Height = 26, FontSize = 14, Style = (Style)FindResource("IconButtonStyle"), ToolTip = Loc.T("editor.sizeDecrease") };
        var plus = new Button { Content = "+", Width = 26, Height = 26, FontSize = 14, Style = (Style)FindResource("IconButtonStyle"), ToolTip = Loc.T("editor.sizeIncrease") };

        minus.Click += (_, _) => AdjustSizeStepper(-1);
        plus.Click += (_, _) => AdjustSizeStepper(1);

        var panel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            ToolTip = Loc.T("editor.size")
        };
        panel.Children.Add(minus);
        panel.Children.Add(_sizeStepperValueText);
        panel.Children.Add(plus);
        return panel;
    }

    /// <summary>True while the stepper should behave as "font size" — a text layer is selected,
    /// or nothing is selected but the Text tool is the one about to be used.</summary>
    private bool IsSizeStepperInTextMode()
    {
        if (TextInputBox.Visibility == Visibility.Visible) return true;
        if (_canvas.CurrentTool == ToolType.Text) return true;
        return _canvas.Selected is { Type: ToolType.Text };
    }

    private void AdjustSizeStepper(int delta)
    {
        var textMode = IsSizeStepperInTextMode();
        var min = textMode ? 8 : 1;
        var max = textMode ? 96 : 30;
        _sizeStepperValue = Math.Clamp(_sizeStepperValue + delta, min, max);
        _sizeStepperValueText.Text = _sizeStepperValue.ToString();
        ApplySizeStepperValue(textMode);
    }

    private void ApplySizeStepperValue(bool textMode)
    {
        if (textMode)
        {
            _currentFontSize = _sizeStepperValue;
            if (TextInputBox.Visibility == Visibility.Visible) TextInputBox.FontSize = _sizeStepperValue;
            _canvas.ApplyFontSizeToSelection(_sizeStepperValue);
            SettingsService.Current.LastFontSize = _sizeStepperValue;
        }
        else
        {
            _canvas.StrokeThickness = _sizeStepperValue;
            _canvas.ApplyThicknessToSelection(_sizeStepperValue);
            SettingsService.Current.LastThickness = _sizeStepperValue;
        }
        SettingsService.Save();
    }

    /// <summary>Keeps the stepper's displayed number in sync with whatever just got selected,
    /// so it never shows a value that doesn't match the layer it would now be changing.</summary>
    private void RefreshSizeStepperDisplay()
    {
        if (_canvas.Selected is not { } selected) return;

        if (selected.Type == ToolType.Text) _sizeStepperValue = (int)Math.Round(selected.FontSize);
        else if (Array.IndexOf(ThicknessTypes, selected.Type) >= 0) _sizeStepperValue = (int)Math.Round(selected.Thickness);
        else return;

        _sizeStepperValueText.Text = _sizeStepperValue.ToString();
    }

    // ---------- Text background ----------

    /// <summary>A "Fondo" toolbar button that opens a small flyout: a color palette plus an
    /// opacity slider, so text can have no background (transparent), a solid color, or anything
    /// in between. Applies live to a selected text layer, and becomes the default for new text.</summary>
    private FrameworkElement BuildBackgroundControl()
    {
        _bgToggleButton = new ToggleButton
        {
            Style = (Style)FindResource("ToolbarButtonStyle"),
            Content = new TextBlock { Text = "▦", FontSize = 16, HorizontalAlignment = System.Windows.HorizontalAlignment.Center },
            ToolTip = Loc.T("editor.background")
        };

        var popup = new Popup
        {
            PlacementTarget = _bgToggleButton,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true
        };

        var popupContent = new Border
        {
            Background = (Brush)FindResource("Brush.Card"),
            BorderBrush = (Brush)FindResource("Brush.ControlBorder"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12),
            Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 2, Opacity = 0.3 }
        };

        var stack = new StackPanel { Width = 170 };

        var title = new TextBlock { Text = Loc.T("editor.background"), FontSize = 11, Margin = new Thickness(0, 0, 0, 6) };
        title.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
        stack.Children.Add(title);

        var swatchWrap = new WrapPanel();

        var noneSwatch = new RadioButton
        {
            Style = (Style)FindResource("SwatchButtonStyle"),
            GroupName = "BgSwatches",
            Tag = new SolidColorBrush(Color.FromRgb(0x8E, 0x8E, 0x93)),
            ToolTip = Loc.T("editor.noBackground")
        };
        noneSwatch.Checked += (_, _) => { _bgOpacitySlider.Value = 0; };
        swatchWrap.Children.Add(noneSwatch);
        var remembersNoFill = _currentTextBackground.A == 0;

        RadioButton? matchingPaletteSwatch = null;
        foreach (var (label, color) in Palette)
        {
            var swatch = new RadioButton
            {
                Style = (Style)FindResource("SwatchButtonStyle"),
                GroupName = "BgSwatches",
                Tag = new SolidColorBrush(color),
                ToolTip = label
            };
            swatch.Checked += (_, _) =>
            {
                _bgBaseColor = color;
                if (_bgOpacitySlider.Value == 0) _bgOpacitySlider.Value = 60;
                else UpdateTextBackgroundFromControls();
            };
            if (!remembersNoFill && color == _bgBaseColor) matchingPaletteSwatch = swatch;
            swatchWrap.Children.Add(swatch);
        }
        stack.Children.Add(swatchWrap);

        var opacityRow = new Grid { Margin = new Thickness(0, 10, 0, 0) };
        opacityRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        opacityRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var opacityLabel = new TextBlock { Text = Loc.T("editor.opacity"), FontSize = 11 };
        opacityLabel.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
        Grid.SetColumn(opacityLabel, 0);
        var opacityValueText = new TextBlock { FontSize = 11, Text = $"{(int)Math.Round(_currentTextBackground.A / 255.0 * 100)}%" };
        opacityValueText.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
        Grid.SetColumn(opacityValueText, 1);
        opacityRow.Children.Add(opacityLabel);
        opacityRow.Children.Add(opacityValueText);
        stack.Children.Add(opacityRow);

        _bgOpacitySlider = new Slider
        {
            Minimum = 0,
            Maximum = 100,
            Value = Math.Round(_currentTextBackground.A / 255.0 * 100),
            TickFrequency = 10,
            Margin = new Thickness(0, 2, 0, 0),
            ToolTip = Loc.T("editor.opacity")
        };
        _bgOpacitySlider.ValueChanged += (_, _) =>
        {
            opacityValueText.Text = $"{(int)Math.Round(_bgOpacitySlider.Value)}%";
            UpdateTextBackgroundFromControls();
        };
        stack.Children.Add(_bgOpacitySlider);

        _bgPreviewSwatch = new Border
        {
            Height = 28,
            Margin = new Thickness(0, 10, 0, 0),
            CornerRadius = new CornerRadius(6),
            BorderBrush = (Brush)FindResource("Brush.ControlBorder"),
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(_currentTextBackground)
        };
        stack.Children.Add(_bgPreviewSwatch);

        if (remembersNoFill || matchingPaletteSwatch == null) noneSwatch.IsChecked = true;
        else matchingPaletteSwatch.IsChecked = true;

        popupContent.Child = stack;
        popup.Child = popupContent;
        BackgroundPopupContentForTesting = popupContent;

        _bgToggleButton.Checked += (_, _) =>
        {
            if (_canvas.Selected is { } selected && Array.IndexOf(FillableAnnotationTypes, selected.Type) >= 0)
            {
                LoadTextBackgroundIntoControls(selected.BackgroundColor);
            }
            popup.IsOpen = true;
        };
        _bgToggleButton.Unchecked += (_, _) => popup.IsOpen = false;
        popup.Closed += (_, _) => _bgToggleButton.IsChecked = false;

        return _bgToggleButton;
    }

    private void UpdateTextBackgroundFromControls()
    {
        var alpha = (byte)Math.Round(_bgOpacitySlider.Value / 100.0 * 255);
        _currentTextBackground = Color.FromArgb(alpha, _bgBaseColor.R, _bgBaseColor.G, _bgBaseColor.B);
        _bgPreviewSwatch.Background = new SolidColorBrush(_currentTextBackground);
        _canvas.FillColor = _currentTextBackground;
        _canvas.ApplyFillToSelection(_currentTextBackground);

        SettingsService.Current.LastFillBaseColor = _bgBaseColor.ToString();
        SettingsService.Current.LastFillOpacity = (int)Math.Round(_bgOpacitySlider.Value);
        SettingsService.Save();
    }

    /// <summary>Loads an existing text layer's background into the Fondo popup's controls (color
    /// swatch + opacity slider) so adjusting it afterward continues from the right state, instead
    /// of silently resetting to whatever was last used elsewhere.</summary>
    private void LoadTextBackgroundIntoControls(Color backgroundColor)
    {
        _currentTextBackground = backgroundColor;
        if (backgroundColor.A > 0) _bgBaseColor = Color.FromRgb(backgroundColor.R, backgroundColor.G, backgroundColor.B);
        _bgOpacitySlider.Value = Math.Round(backgroundColor.A / 255.0 * 100);
        _bgPreviewSwatch.Background = new SolidColorBrush(_currentTextBackground);
    }

    // ---------- Text alignment ----------

    /// <summary>An "Alinear" toolbar button that opens a flyout with horizontal (left/center/right)
    /// and vertical (top/center/bottom) alignment choices — applies to a selected text/rectangle/
    /// ellipse layer live, and becomes the default for new text.</summary>
    private FrameworkElement BuildTextAlignControl()
    {
        _alignToggleButton = new ToggleButton
        {
            Style = (Style)FindResource("ToolbarButtonStyle"),
            Content = new TextBlock { Text = "≣", FontSize = 16, HorizontalAlignment = System.Windows.HorizontalAlignment.Center },
            ToolTip = Loc.T("editor.align")
        };

        var popup = new Popup
        {
            PlacementTarget = _alignToggleButton,
            Placement = PlacementMode.Bottom,
            StaysOpen = false,
            AllowsTransparency = true
        };

        var popupContent = new Border
        {
            Background = (Brush)FindResource("Brush.Card"),
            BorderBrush = (Brush)FindResource("Brush.ControlBorder"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12),
            Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 2, Opacity = 0.3 }
        };

        var stack = new StackPanel { Width = 170 };

        var hTitle = new TextBlock { Text = Loc.T("editor.alignHorizontal"), FontSize = 11, Margin = new Thickness(0, 0, 0, 6) };
        hTitle.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
        stack.Children.Add(hTitle);

        var hRow = new StackPanel { Orientation = Orientation.Horizontal };
        _hAlignButtons.Clear();
        foreach (var (align, glyph, tipKey) in new[]
                 {
                     (TextHAlign.Left, "⯇", "editor.alignLeft"),
                     (TextHAlign.Center, "▤", "editor.alignCenterH"),
                     (TextHAlign.Right, "⯈", "editor.alignRight")
                 })
        {
            var button = new RadioButton
            {
                Style = (Style)FindResource("ToolbarButtonStyle"),
                GroupName = "HAlign",
                Content = new TextBlock { Text = glyph, FontSize = 14 },
                ToolTip = Loc.T(tipKey),
                Margin = new Thickness(0, 0, 4, 0)
            };
            var captured = align;
            button.Checked += (_, _) =>
            {
                _currentTextHAlign = captured;
                _canvas.ApplyTextAlignToSelection(hAlign: captured);
                SettingsService.Current.LastTextHAlign = captured.ToString();
                SettingsService.Save();
            };
            _hAlignButtons[align] = button;
            hRow.Children.Add(button);
        }
        stack.Children.Add(hRow);

        var vTitle = new TextBlock { Text = Loc.T("editor.alignVertical"), FontSize = 11, Margin = new Thickness(0, 10, 0, 6) };
        vTitle.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextSecondary");
        stack.Children.Add(vTitle);

        var vRow = new StackPanel { Orientation = Orientation.Horizontal };
        _vAlignButtons.Clear();
        foreach (var (align, glyph, tipKey) in new[]
                 {
                     (TextVAlign.Top, "⬒", "editor.alignTop"),
                     (TextVAlign.Center, "▤", "editor.alignCenterV"),
                     (TextVAlign.Bottom, "⬓", "editor.alignBottom")
                 })
        {
            var button = new RadioButton
            {
                Style = (Style)FindResource("ToolbarButtonStyle"),
                GroupName = "VAlign",
                Content = new TextBlock { Text = glyph, FontSize = 14 },
                ToolTip = Loc.T(tipKey),
                Margin = new Thickness(0, 0, 4, 0)
            };
            var captured = align;
            button.Checked += (_, _) =>
            {
                _currentTextVAlign = captured;
                _canvas.ApplyTextAlignToSelection(vAlign: captured);
                SettingsService.Current.LastTextVAlign = captured.ToString();
                SettingsService.Save();
            };
            _vAlignButtons[align] = button;
            vRow.Children.Add(button);
        }
        stack.Children.Add(vRow);

        _hAlignButtons[_currentTextHAlign].IsChecked = true;
        _vAlignButtons[_currentTextVAlign].IsChecked = true;

        popupContent.Child = stack;
        popup.Child = popupContent;

        _alignToggleButton.Checked += (_, _) =>
        {
            if (_canvas.Selected is { } selected && Array.IndexOf(FillableAnnotationTypes, selected.Type) >= 0)
            {
                _hAlignButtons[selected.TextHAlign].IsChecked = true;
                _vAlignButtons[selected.TextVAlign].IsChecked = true;
            }
            popup.IsOpen = true;
        };
        _alignToggleButton.Unchecked += (_, _) => popup.IsOpen = false;
        popup.Closed += (_, _) => _alignToggleButton.IsChecked = false;

        return _alignToggleButton;
    }

    private void SelectTool(ToolType tool)
    {
        _canvas.CurrentTool = tool;
        foreach (var (t, button) in _toolButtons) button.IsChecked = t == tool;
    }

    private void UpdateUndoRedoState()
    {
        _undoButton.IsEnabled = _canvas.CanUndo;
        _redoButton.IsEnabled = _canvas.CanRedo;
    }

    // ---------- Image import ----------

    private void OnImportImageClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Imágenes|*.png;*.jpg;*.jpeg;*.bmp;*.gif"
        };
        if (dialog.ShowDialog(this) != true) return;

        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(dialog.FileName);
            image.EndInit();
            image.Freeze();
            _canvas.AddImageAnnotation(image);
            SelectTool(ToolType.Select);
            _canvas.Focus();
        }
        catch (Exception ex)
        {
            Logger.Log($"EditorWindow.OnImportImageClick failed: {ex.Message}");
        }
    }

    // ---------- Layers panel ----------

    private readonly Dictionary<Border, Annotation> _layerRows = new();
    private Annotation? _draggedLayer;
    private Border? _dragHoverRow;
    private Brush? _dragHoverOriginalBackground;

    private void RefreshLayersPanel()
    {
        LayersPanel.Children.Clear();
        _layerRows.Clear();
        _draggedLayer = null;
        _dragHoverRow = null;
        var annotations = _canvas.Annotations;

        if (annotations.Count == 0)
        {
            LayersPanel.Children.Add(new TextBlock
            {
                Text = Loc.T("layers.empty"),
                FontSize = 12,
                Margin = new Thickness(6, 8, 6, 0),
                TextWrapping = TextWrapping.Wrap,
                Foreground = (Brush)FindResource("Brush.TextSecondary")
            });
            return;
        }

        // Front-most layer (last in the back-to-front list) shown at the top, like most editors.
        for (var i = annotations.Count - 1; i >= 0; i--)
        {
            var annotation = annotations[i];
            var row = BuildLayerRow(annotation, i);
            _layerRows[row] = annotation;
            LayersPanel.Children.Add(row);
        }
    }

    /// <summary>Walks up from a hit-test result to the nearest ancestor that is one of our own
    /// layer rows (skipping internal Borders that live inside a button's own control template).</summary>
    private Border? FindRowAt(Point panelPoint)
    {
        var obj = VisualTreeHelper.HitTest(LayersPanel, panelPoint)?.VisualHit as DependencyObject;
        while (obj != null)
        {
            if (obj is Border border && _layerRows.ContainsKey(border)) return border;
            if (ReferenceEquals(obj, LayersPanel)) break;
            obj = VisualTreeHelper.GetParent(obj);
        }
        return null;
    }

    private static bool IsInsideButton(object? originalSource)
    {
        var obj = originalSource as DependencyObject;
        while (obj != null)
        {
            if (obj is Button) return true;
            obj = VisualTreeHelper.GetParent(obj);
        }
        return false;
    }

    private Border BuildLayerRow(Annotation annotation, int index)
    {
        var isSelected = ReferenceEquals(annotation, _canvas.Selected);
        var typeLabel = LayerTypeKeys.TryGetValue(annotation.Type, out var key) ? Loc.T(key) : annotation.Type.ToString();
        if (annotation.Type == ToolType.Text && !string.IsNullOrWhiteSpace(annotation.Text))
        {
            typeLabel = annotation.Text.Length > 16 ? annotation.Text[..16] + "…" : annotation.Text;
        }

        var row = new Border
        {
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(8, 6, 6, 6),
            Margin = new Thickness(0, 0, 0, 4),
            Background = isSelected ? (Brush)FindResource("Brush.SelectedOverlay") : Brushes.Transparent,
            Cursor = System.Windows.Input.Cursors.SizeAll
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = new TextBlock
        {
            Text = typeLabel,
            FontSize = 12,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Opacity = annotation.Locked ? 0.5 : 1.0
        };
        label.SetResourceReference(TextBlock.ForegroundProperty, "Brush.TextPrimary");
        Grid.SetColumn(label, 0);
        grid.Children.Add(label);

        var upButton = new Button { Content = "▲", FontSize = 9, Width = 22, Height = 22, Style = (Style)FindResource("IconButtonStyle"), ToolTip = Loc.T("layers.moveUp") };
        upButton.Click += (_, e) => { e.Handled = true; _canvas.MoveLayer(annotation, 1); };
        Grid.SetColumn(upButton, 1);
        grid.Children.Add(upButton);

        var downButton = new Button { Content = "▼", FontSize = 9, Width = 22, Height = 22, Style = (Style)FindResource("IconButtonStyle"), ToolTip = Loc.T("layers.moveDown") };
        downButton.Click += (_, e) => { e.Handled = true; _canvas.MoveLayer(annotation, -1); };
        Grid.SetColumn(downButton, 2);
        grid.Children.Add(downButton);

        var lockButton = new Button
        {
            Content = annotation.Locked ? "🔒" : "🔓",
            FontSize = 11,
            Width = 22,
            Height = 22,
            Style = (Style)FindResource("IconButtonStyle"),
            ToolTip = annotation.Locked ? Loc.T("layers.unlock") : Loc.T("layers.lock")
        };
        lockButton.Click += (_, e) => { e.Handled = true; _canvas.ToggleLock(annotation); };
        Grid.SetColumn(lockButton, 3);
        grid.Children.Add(lockButton);

        var deleteButton = new Button { Content = "✕", FontSize = 10, Width = 22, Height = 22, Style = (Style)FindResource("IconButtonStyle"), ToolTip = Loc.T("layers.delete") };
        deleteButton.Click += (_, e) => { e.Handled = true; _canvas.DeleteAnnotationExternally(annotation); };
        Grid.SetColumn(deleteButton, 4);
        grid.Children.Add(deleteButton);

        row.Child = grid;

        row.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (IsInsideButton(e.OriginalSource)) return;
            _draggedLayer = annotation;
            _dragHoverRow = null;
            row.CaptureMouse();
        };

        row.PreviewMouseMove += (_, e) =>
        {
            if (_draggedLayer == null || !ReferenceEquals(Mouse.Captured, row) || e.LeftButton != MouseButtonState.Pressed) return;

            var hoverRow = FindRowAt(e.GetPosition(LayersPanel));
            if (ReferenceEquals(hoverRow, _dragHoverRow)) return;

            if (_dragHoverRow != null) _dragHoverRow.Background = _dragHoverOriginalBackground!;
            _dragHoverRow = hoverRow != row ? hoverRow : null;
            if (_dragHoverRow != null)
            {
                _dragHoverOriginalBackground = _dragHoverRow.Background;
                _dragHoverRow.Background = (Brush)FindResource("Brush.HoverOverlay");
            }
        };

        row.PreviewMouseLeftButtonUp += (_, _) =>
        {
            if (_draggedLayer == null) return;
            row.ReleaseMouseCapture();

            var dragged = _draggedLayer;
            var dropTarget = _dragHoverRow != null && _layerRows.TryGetValue(_dragHoverRow, out var targetAnnotation)
                ? targetAnnotation
                : null;

            _draggedLayer = null;
            _dragHoverRow = null;

            if (dropTarget != null && dropTarget != dragged)
            {
                _canvas.ReorderAnnotation(dragged, dropTarget);
            }
            else
            {
                _canvas.SelectAnnotationExternally(dragged);
            }

            _canvas.Focus();
            RefreshLayersPanel();
        };

        return row;
    }

    // ---------- Text tool ----------

    private const double TextBoxDefaultWidth = 320;
    private const double TextBoxDefaultHeight = 140;

    private Point _textInputOrigin;
    private Annotation? _editingTextAnnotation;

    /// <summary>Converts a canvas-local point — the space <see cref="Annotation.Bounds"/> lives in —
    /// into CanvasContainer-local coordinates, the space the text box/crop-confirm overlay controls
    /// are positioned in via Margin. The two aren't the same point whenever the canvas renders
    /// smaller than CanvasContainer's own allotted space (it then centers within it, e.g. any time
    /// the screenshot is narrower/shorter than the maximized editor window) — using a canvas-local
    /// point as an overlay control's raw Margin then drifts it away from the shape/selection it's
    /// supposed to sit over by exactly that centering offset.</summary>
    private Point CanvasToOverlay(Point canvasLocalPoint) => _canvas.TranslatePoint(canvasLocalPoint, CanvasContainer);

    /// <summary>Shows the big text box centered in the currently visible part of the canvas, so
    /// picking the Text tool is enough to start typing — no precise click needed first.</summary>
    private void ShowCenteredTextBox()
    {
        var centerX = CanvasScroll.HorizontalOffset + CanvasScroll.ViewportWidth / 2 - 24;
        var centerY = CanvasScroll.VerticalOffset + CanvasScroll.ViewportHeight / 2 - 24;

        var maxW = _canvas.Background.PixelWidth;
        var maxH = _canvas.Background.PixelHeight;

        var x = Math.Clamp(centerX - TextBoxDefaultWidth / 2, 0, Math.Max(0, maxW - TextBoxDefaultWidth));
        var y = Math.Clamp(centerY - TextBoxDefaultHeight / 2, 0, Math.Max(0, maxH - TextBoxDefaultHeight));

        OnCanvasTextToolClicked(new Point(x, y));
    }

    private void OnCanvasTextToolClicked(Point location)
    {
        _editingTextAnnotation = null;
        _textInputOrigin = location;
        TextInputBox.Text = string.Empty;
        TextInputBox.FontSize = _currentFontSize;
        TextInputBox.Foreground = new SolidColorBrush(_currentColor);
        TextInputBox.Background = Brushes.White;
        TextInputBox.Width = TextBoxDefaultWidth;
        TextInputBox.Height = TextBoxDefaultHeight;
        TextInputBox.TextAlignment = _currentTextHAlign switch
        {
            TextHAlign.Left => System.Windows.TextAlignment.Left,
            TextHAlign.Right => System.Windows.TextAlignment.Right,
            _ => System.Windows.TextAlignment.Center
        };
        TextInputBox.VerticalContentAlignment = _currentTextVAlign switch
        {
            TextVAlign.Top => System.Windows.VerticalAlignment.Top,
            TextVAlign.Bottom => System.Windows.VerticalAlignment.Bottom,
            _ => System.Windows.VerticalAlignment.Center
        };
        var overlayLocation = CanvasToOverlay(location);
        TextInputBox.Margin = new Thickness(overlayLocation.X, overlayLocation.Y, 0, 0);
        TextInputBox.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        TextInputBox.VerticalAlignment = System.Windows.VerticalAlignment.Top;
        TextInputBox.Visibility = Visibility.Visible;
        FocusTextInputBoxDeferred();
    }

    /// <summary>
    /// WPF reassigns keyboard focus to whatever element was originally hit-tested for a mouse
    /// button press right after the input event's handlers finish — which silently overrides a
    /// `Focus()` call made *during* that same mouse-down handler (exactly what both text-entry
    /// paths do, since they're triggered from the canvas's own MouseLeftButtonDown). Deferring to
    /// Input priority runs after that default reassignment instead of racing it.
    /// </summary>
    private void FocusTextInputBoxDeferred()
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (TextInputBox.Visibility != Visibility.Visible) return;
            TextInputBox.Focus();
            Keyboard.Focus(TextInputBox);
        }), System.Windows.Threading.DispatcherPriority.Input);
    }

    /// <summary>Double-clicking an existing text layer, or a rectangle/ellipse, reopens it for
    /// editing in place — for a shape, the box matches its bounds exactly so the label can't drift
    /// away from what it's labeling.</summary>
    private void OnTextEditRequested(Annotation annotation)
    {
        Logger.Log($"EditorWindow: OnTextEditRequested for \"{annotation.Text}\" (type={annotation.Type})");
        _editingTextAnnotation = annotation;
        _textInputOrigin = annotation.Bounds.TopLeft;
        TextInputBox.Text = annotation.Text;
        TextInputBox.FontSize = annotation.FontSize;
        TextInputBox.Foreground = new SolidColorBrush(
            annotation.Type == ToolType.Text ? annotation.Color : annotation.LabelColor);
        _currentTextHAlign = annotation.TextHAlign;
        _currentTextVAlign = annotation.TextVAlign;
        TextInputBox.TextAlignment = annotation.TextHAlign switch
        {
            TextHAlign.Left => System.Windows.TextAlignment.Left,
            TextHAlign.Right => System.Windows.TextAlignment.Right,
            _ => System.Windows.TextAlignment.Center
        };
        TextInputBox.VerticalContentAlignment = annotation.TextVAlign switch
        {
            TextVAlign.Top => System.Windows.VerticalAlignment.Top,
            TextVAlign.Bottom => System.Windows.VerticalAlignment.Bottom,
            _ => System.Windows.VerticalAlignment.Center
        };

        if (annotation.Type == ToolType.Text)
        {
            TextInputBox.Background = Brushes.White;
            LoadTextBackgroundIntoControls(annotation.BackgroundColor);
            TextInputBox.Width = Math.Max(TextBoxDefaultWidth, annotation.Bounds.Width);
            TextInputBox.Height = Math.Max(TextBoxDefaultHeight, annotation.Bounds.Height);
        }
        else
        {
            // A shape's own fill/outline should stay visible while typing its label — an opaque
            // white box here would cover the shape entirely and look like a second one on top of it.
            TextInputBox.Background = Brushes.Transparent;
            TextInputBox.Width = annotation.Bounds.Width;
            TextInputBox.Height = annotation.Bounds.Height;
        }
        var overlayOrigin = CanvasToOverlay(_textInputOrigin);
        TextInputBox.Margin = new Thickness(overlayOrigin.X, overlayOrigin.Y, 0, 0);
        TextInputBox.HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        TextInputBox.VerticalAlignment = System.Windows.VerticalAlignment.Top;
        TextInputBox.Visibility = Visibility.Visible;
        FocusTextInputBoxDeferred();
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (TextInputBox.Visibility == Visibility.Visible) TextInputBox.SelectAll();
        }), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void OnTextInputKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            TextInputBox.Visibility = Visibility.Collapsed;
            _editingTextAnnotation = null;
            SelectTool(ToolType.Select);
        }
        else if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            CommitTextInput();
        }
    }

    private void OnTextInputLostFocus(object sender, RoutedEventArgs e)
    {
        Logger.Log("EditorWindow: TextInputBox lost focus, committing.");
        CommitTextInput();
    }

    private void CommitTextInput()
    {
        if (TextInputBox.Visibility != Visibility.Visible) return;
        var text = TextInputBox.Text;
        var fontSize = TextInputBox.FontSize;
        var color = ((SolidColorBrush)TextInputBox.Foreground).Color;
        var bounds = new Rect(_textInputOrigin, new Size(TextInputBox.Width, TextInputBox.Height));
        TextInputBox.Visibility = Visibility.Collapsed;

        if (_editingTextAnnotation != null)
        {
            if (_editingTextAnnotation.Type == ToolType.Text)
            {
                if (string.IsNullOrWhiteSpace(text)) _canvas.DeleteAnnotationExternally(_editingTextAnnotation);
                else _canvas.UpdateTextAnnotation(_editingTextAnnotation, text, bounds, fontSize, color, _currentTextBackground, _currentTextHAlign, _currentTextVAlign);
            }
            else
            {
                // A shape's embedded label is allowed to be empty — clearing it never deletes the shape.
                _canvas.UpdateShapeText(_editingTextAnnotation, text, fontSize, _currentTextHAlign, _currentTextVAlign);
            }
            _editingTextAnnotation = null;
        }
        else if (!string.IsNullOrWhiteSpace(text))
        {
            _canvas.AddTextAnnotation(bounds, text, fontSize, color, _currentTextBackground, _currentTextHAlign, _currentTextVAlign);
        }
        SelectTool(ToolType.Select);
    }

    // ---------- Crop ----------

    private void OnCropProposed(Rect? rect)
    {
        if (rect is { } r)
        {
            var overlayOrigin = CanvasToOverlay(new Point(r.X, r.Bottom + 8));
            CropConfirmPanel.Visibility = Visibility.Visible;
            CropConfirmPanel.Margin = new Thickness(overlayOrigin.X, overlayOrigin.Y, 0, 0);
        }
        else
        {
            CropConfirmPanel.Visibility = Visibility.Collapsed;
        }
    }

    private void OnApplyCropClick(object sender, RoutedEventArgs e)
    {
        if (_canvas.PendingCrop is { } rect) _canvas.ApplyCrop(rect);
        CropConfirmPanel.Visibility = Visibility.Collapsed;
        SelectTool(ToolType.Select);
    }

    private void OnCancelCropClick(object sender, RoutedEventArgs e)
    {
        _canvas.CancelCrop();
        CropConfirmPanel.Visibility = Visibility.Collapsed;
        SelectTool(ToolType.Select);
    }

    // ---------- Save / Copy / Close ----------

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        ClipboardService.SetImage(_canvas.RenderFlattened());
        StatusText.Text = Loc.T("editor.copiedToClipboard");
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        try
        {
            var settings = SettingsService.Current;
            Directory.CreateDirectory(settings.SaveFolder);
            var path = _savedFilePath ?? Path.Combine(settings.SaveFolder, $"ShotX_{DateTime.Now:yyyyMMdd_HHmmss}.png");
            SaveToFile(path);
            _savedFilePath = path;
            _hasUnsavedChanges = false;
            StatusText.Text = string.Format(Loc.T("editor.savedAt"), path);
        }
        catch (Exception ex)
        {
            Logger.Log($"EditorWindow.OnSaveClick failed: {ex.Message}");
            StatusText.Text = Loc.T("editor.saveFailed");
        }
    }

    private void OnSaveAsClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "PNG|*.png|JPEG|*.jpg",
            FileName = $"ShotX_{DateTime.Now:yyyyMMdd_HHmmss}.png",
            InitialDirectory = Directory.Exists(SettingsService.Current.SaveFolder)
                ? SettingsService.Current.SaveFolder
                : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
        };
        if (dialog.ShowDialog(this) == true)
        {
            try
            {
                SaveToFile(dialog.FileName);
                _savedFilePath = dialog.FileName;
                _hasUnsavedChanges = false;
                StatusText.Text = string.Format(Loc.T("editor.savedAt"), dialog.FileName);
            }
            catch (Exception ex)
            {
                Logger.Log($"EditorWindow.OnSaveAsClick failed: {ex.Message}");
                StatusText.Text = Loc.T("editor.saveFailed");
            }
        }
    }

    private void SaveToFile(string path)
    {
        var rtb = _canvas.RenderFlattened();
        BitmapEncoder encoder = path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
                                 path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase)
            ? new JpegBitmapEncoder { QualityLevel = 92 }
            : new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
        encoder.Save(stream);
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        if (_hasUnsavedChanges)
        {
            var result = System.Windows.MessageBox.Show(this, Loc.T("editor.unsavedChanges"),
                "ShotX", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result != MessageBoxResult.Yes) return;
        }
        Close();
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        var ctrl = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
        var textEditing = TextInputBox.Visibility == Visibility.Visible;

        if (ctrl && e.Key == Key.Z) { _canvas.Undo(); UpdateUndoRedoState(); RefreshLayersPanel(); e.Handled = true; }
        else if (ctrl && (e.Key == Key.Y)) { _canvas.Redo(); UpdateUndoRedoState(); RefreshLayersPanel(); e.Handled = true; }
        else if (ctrl && e.Key == Key.C && !textEditing)
        {
            // With a shape selected, Ctrl+C clones it (in-app clipboard) instead of copying the
            // flattened image — the whole-image copy remains the fallback with nothing selected.
            if (_canvas.HasSelection) _canvas.CopySelectedToClipboard();
            else OnCopyClick(this, new RoutedEventArgs());
            e.Handled = true;
        }
        else if (ctrl && e.Key == Key.V && !textEditing) { _canvas.PasteFromClipboard(); e.Handled = true; }
        else if (ctrl && e.Key == Key.D && !textEditing) { _canvas.DuplicateSelected(); e.Handled = true; }
        else if (ctrl && e.Key == Key.S) { OnSaveClick(this, new RoutedEventArgs()); e.Handled = true; }
        else if ((e.Key == Key.Delete || e.Key == Key.Back) && !textEditing && _canvas.Selected is { } selected)
        {
            // Safety net alongside DrawingCanvasControl's own OnKeyDown, in case focus is on some
            // other toolbar control when Delete is pressed rather than the canvas itself.
            _canvas.DeleteAnnotationExternally(selected);
            e.Handled = true;
        }
        base.OnPreviewKeyDown(e);
    }

    private static readonly ToolType[] TypableShapeTypes = { ToolType.Rectangle, ToolType.Ellipse };

    /// <summary>Typing while a rectangle/ellipse is selected (Select tool, nothing already being
    /// edited) opens its label for editing and starts it off with whatever was just typed — no
    /// double-click needed first.</summary>
    protected override void OnPreviewTextInput(TextCompositionEventArgs e)
    {
        if (TextInputBox.Visibility != Visibility.Visible
            && _canvas.CurrentTool != ToolType.Text
            && _canvas.Selected is { Locked: false } selected
            && Array.IndexOf(TypableShapeTypes, selected.Type) >= 0
            && !string.IsNullOrEmpty(e.Text) && e.Text != "\r" && e.Text != "\u001b" && e.Text != "\t")
        {
            var typedText = e.Text;
            OnTextEditRequested(selected);
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (TextInputBox.Visibility != Visibility.Visible) return;
                TextInputBox.Text = typedText;
                TextInputBox.CaretIndex = TextInputBox.Text.Length;
            }), System.Windows.Threading.DispatcherPriority.Input);
            e.Handled = true;
        }
        base.OnPreviewTextInput(e);
    }
}
