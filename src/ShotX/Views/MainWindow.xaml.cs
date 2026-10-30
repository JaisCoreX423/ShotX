using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ShotX.Services;

namespace ShotX.Views;

/// <summary>
/// The app's home window: shown when the tray icon or a pinned taskbar shortcut is clicked.
/// From here the user decides what to do next (capture, open an image, settings) instead of
/// jumping straight into the editor. Closing it (X) just hides it back to the tray; only the
/// tray "Salir" menu item, or <see cref="ForceClose"/> set beforehand, actually exits the app.
/// </summary>
public partial class MainWindow : Window
{
    public bool ForceClose { get; set; }

    public event Action? CaptureRegionRequested;
    public event Action? CaptureFullScreenRequested;
    public event Action? CaptureWindowRequested;
    public event Action? OpenImageRequested;
    public event Action? OpenFolderRequested;
    public event Action? SettingsRequested;

    public MainWindow()
    {
        InitializeComponent();
        MaximizeBoundsFix.Apply(this);
        ApplyLocalization();
        RefreshHotkeyLabels();
        StateChanged += (_, _) => UpdateMaximizeGlyph();
        Loaded += (_, _) => PlayOpenAnimation();
        Loc.LanguageChanged += ApplyLocalization;
    }

    private void ApplyLocalization()
    {
        WhatToDoText.Text = Loc.T("main.whatToDo");
        RegionLabel.Text = Loc.T("main.region");
        FullScreenLabel.Text = Loc.T("main.fullScreen");
        WindowLabel.Text = Loc.T("main.activeWindow");
        OpenImageButton.Content = Loc.T("main.openImage");
        OpenFolderButton.Content = Loc.T("main.openFolder");
        SettingsButton.Content = Loc.T("main.settings");
        MinimizeButton.ToolTip = Loc.T("window.minimize");
        MaximizeButton.ToolTip = WindowState == WindowState.Maximized ? Loc.T("window.restore") : Loc.T("window.maximize");
        CloseButton.ToolTip = Loc.T("window.close");
        RefreshHotkeyLabels();
    }

    public void RefreshHotkeyLabels()
    {
        var settings = SettingsService.Current;
        RegionHotkeyLabel.Text = settings.RegionHotkey.ToString();
        FullScreenHotkeyLabel.Text = settings.FullScreenHotkey.ToString();
        WindowHotkeyLabel.Text = settings.WindowHotkey.ToString();
    }

    public void ShowAndActivate()
    {
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
    }

    private void PlayOpenAnimation()
    {
        Opacity = 0;
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        var scaleX = new DoubleAnimation(0.95, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        var scaleY = new DoubleAnimation(0.95, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        BeginAnimation(OpacityProperty, fade);
        WindowScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
        WindowScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
    }

    private void UpdateMaximizeGlyph()
    {
        var isMaximized = WindowState == WindowState.Maximized;
        MaximizeButton.Content = isMaximized ? "" : "";
        MaximizeButton.ToolTip = isMaximized ? Loc.T("window.restore") : Loc.T("window.maximize");
    }

    private void OnMinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximizeClick(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!ForceClose)
        {
            e.Cancel = true;
            Hide();
            return;
        }
        base.OnClosing(e);
    }

    private void OnRegionClick(object sender, RoutedEventArgs e) => CaptureRegionRequested?.Invoke();
    private void OnFullScreenClick(object sender, RoutedEventArgs e) => CaptureFullScreenRequested?.Invoke();
    private void OnWindowClick(object sender, RoutedEventArgs e) => CaptureWindowRequested?.Invoke();
    private void OnOpenImageClick(object sender, RoutedEventArgs e) => OpenImageRequested?.Invoke();
    private void OnOpenFolderClick(object sender, RoutedEventArgs e) => OpenFolderRequested?.Invoke();
    private void OnSettingsClick(object sender, RoutedEventArgs e) => SettingsRequested?.Invoke();
}
