using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media.Animation;
using ShotX.Models;
using ShotX.Services;
using Button = System.Windows.Controls.Button;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace ShotX.Views;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly Dictionary<AppTheme, ToggleButton> _themeButtons = new();
    private readonly Dictionary<string, ToggleButton> _languageButtons = new();
    private bool _initializing = true;

    /// <summary>Fires after a hotkey is rebound and saved, so the running app can
    /// unregister/re-register its global hotkeys with the new combo.</summary>
    public event Action? HotkeysChanged;

    private Button? _recordingButton;
    private string? _recordingField;

    public SettingsWindow()
    {
        InitializeComponent();
        _settings = SettingsService.Current;

        BuildLanguageSelector();
        BuildThemeSelector();
        ApplyLocalization();
        LoadValues();
        WireChangeHandlers();

        _initializing = false;
        Loaded += (_, _) => PlayOpenAnimation();
    }

    private void ApplyLocalization()
    {
        Title = $"ShotX - {Loc.T("settings.title")}";
        TitleText.Text = Loc.T("settings.title");
        AppearanceHeader.Text = Loc.T("settings.appearance");
        LanguageLabel.Text = Loc.T("settings.language");
        ThemeLabel.Text = Loc.T("settings.theme");
        CaptureHeader.Text = Loc.T("settings.capture");
        AutoCopyLabel.Text = Loc.T("settings.autoCopy");
        AutoOpenEditorLabel.Text = Loc.T("settings.autoOpenEditor");
        PlaySoundLabel.Text = Loc.T("settings.playSound");
        HotkeysHeader.Text = Loc.T("settings.hotkeys");
        HotkeyRegionLabel.Text = Loc.T("settings.hotkeyRegion");
        HotkeyFullScreenLabel.Text = Loc.T("settings.hotkeyFullScreen");
        HotkeyWindowLabel.Text = Loc.T("settings.hotkeyWindow");
        SavingHeader.Text = Loc.T("settings.saving");
        SaveFolderLabel.Text = Loc.T("settings.saveFolder");
        ChooseFolderButton.Content = Loc.T("settings.chooseFolder");
        StartupHeader.Text = Loc.T("settings.startup");
        StartWithWindowsLabel.Text = Loc.T("settings.startWithWindows");
        CloseButton.ToolTip = Loc.T("window.close");

        foreach (var (theme, button) in _themeButtons)
        {
            button.Content = theme switch
            {
                AppTheme.Light => Loc.T("settings.themeLight"),
                AppTheme.Dark => Loc.T("settings.themeDark"),
                _ => Loc.T("settings.themeSystem")
            };
        }

        RefreshHotkeyButtons();
    }

    private void RefreshHotkeyButtons()
    {
        RegionHotkeyButton.Content = _settings.RegionHotkey.ToString();
        FullScreenHotkeyButton.Content = _settings.FullScreenHotkey.ToString();
        WindowHotkeyButton.Content = _settings.WindowHotkey.ToString();
        HotkeyHintText.Text = string.Empty;
    }

    // ---------- Hotkey recording ----------

    private void OnHotkeyButtonClick(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        CancelHotkeyRecording();

        _recordingButton = button;
        _recordingField = button.Name switch
        {
            nameof(RegionHotkeyButton) => "region",
            nameof(FullScreenHotkeyButton) => "fullscreen",
            nameof(WindowHotkeyButton) => "window",
            _ => null
        };
        button.Content = Loc.T("settings.pressKeys");
        HotkeyHintText.Text = Loc.T("settings.pressKeysHint");
        Focus();
    }

    private void CancelHotkeyRecording()
    {
        if (_recordingButton == null) return;
        _recordingButton = null;
        _recordingField = null;
        RefreshHotkeyButtons();
    }

    private static bool IsModifierKey(Key key) => key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt
        or Key.LeftShift or Key.RightShift or Key.LWin or Key.RWin or Key.System or Key.None;

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (_recordingField == null)
        {
            base.OnPreviewKeyDown(e);
            return;
        }

        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        ProcessCapturedKey(key, Keyboard.Modifiers);
    }

    /// <summary>Core of hotkey capture, split out from <see cref="OnPreviewKeyDown"/> so it can be
    /// driven directly (by a test) without needing a real routed KeyEventArgs.</summary>
    private void ProcessCapturedKey(Key key, ModifierKeys modifiers)
    {
        if (_recordingField == null) return;

        if (key == Key.Escape)
        {
            CancelHotkeyRecording();
            return;
        }

        if (IsModifierKey(key)) return;

        var binding = new HotkeyBinding(modifiers, key);

        var others = new (string Field, HotkeyBinding Binding)[]
        {
            ("region", _settings.RegionHotkey),
            ("fullscreen", _settings.FullScreenHotkey),
            ("window", _settings.WindowHotkey)
        };
        if (others.Any(o => o.Field != _recordingField && o.Binding.Modifiers == binding.Modifiers && o.Binding.Key == binding.Key))
        {
            HotkeyHintText.Text = Loc.T("settings.hotkeyDuplicate");
            return;
        }

        switch (_recordingField)
        {
            case "region": _settings.RegionHotkey = binding; break;
            case "fullscreen": _settings.FullScreenHotkey = binding; break;
            case "window": _settings.WindowHotkey = binding; break;
        }
        SettingsService.Save(_settings);

        _recordingButton = null;
        _recordingField = null;
        RefreshHotkeyButtons();
        HotkeysChanged?.Invoke();
    }

    public void SimulateHotkeyClickForTesting(string field)
    {
        var button = field switch
        {
            "region" => RegionHotkeyButton,
            "fullscreen" => FullScreenHotkeyButton,
            "window" => WindowHotkeyButton,
            _ => null
        };
        if (button != null) OnHotkeyButtonClick(button, new RoutedEventArgs());
    }

    public void SimulateHotkeyCaptureForTesting(ModifierKeys modifiers, Key key) => ProcessCapturedKey(key, modifiers);

    private void PlayOpenAnimation()
    {
        Opacity = 0;
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        var scale = new DoubleAnimation(0.95, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        var scale2 = new DoubleAnimation(0.95, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        BeginAnimation(OpacityProperty, fade);
        WindowScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scale);
        WindowScale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scale2);
    }

    private void BuildLanguageSelector()
    {
        var options = new (string Code, string Label)[]
        {
            ("es", "Español"),
            ("en", "English"),
        };

        foreach (var (code, label) in options)
        {
            var button = new ToggleButton
            {
                Content = label,
                Width = 110,
                Height = 32,
                Margin = new Thickness(0, 0, 8, 0),
                Style = (Style)FindResource("ToolbarButtonStyle")
            };
            button.Click += (_, _) =>
            {
                foreach (var b in _languageButtons.Values) b.IsChecked = false;
                button.IsChecked = true;
                Loc.SetLanguage(code);
                ApplyLocalization();
            };
            _languageButtons[code] = button;
            LanguagePanel.Children.Add(button);
        }
    }

    private void BuildThemeSelector()
    {
        var options = new (AppTheme Theme, string Label)[]
        {
            (AppTheme.System, "Sistema"),
            (AppTheme.Light, "Claro"),
            (AppTheme.Dark, "Oscuro"),
        };

        foreach (var (theme, label) in options)
        {
            var button = new ToggleButton
            {
                Content = label,
                Width = 110,
                Height = 32,
                Margin = new Thickness(0, 0, 8, 0),
                Style = (Style)FindResource("ToolbarButtonStyle")
            };
            button.Click += (_, _) =>
            {
                foreach (var b in _themeButtons.Values) b.IsChecked = false;
                button.IsChecked = true;
                _settings.Theme = theme;
                ThemeService.Apply(theme);
                SettingsService.Save(_settings);
            };
            _themeButtons[theme] = button;
            ThemePanel.Children.Add(button);
        }
    }

    private void LoadValues()
    {
        if (_themeButtons.TryGetValue(_settings.Theme, out var current)) current.IsChecked = true;
        if (_languageButtons.TryGetValue(_settings.Language, out var currentLang)) currentLang.IsChecked = true;

        AutoCopyCheck.IsChecked = _settings.AutoCopyToClipboard;
        AutoOpenEditorCheck.IsChecked = _settings.AutoOpenEditor;
        PlaySoundCheck.IsChecked = _settings.PlayCaptureSound;
        StartWithWindowsCheck.IsChecked = _settings.StartWithWindows;
        SaveFolderBox.Text = _settings.SaveFolder;
    }

    private void WireChangeHandlers()
    {
        AutoCopyCheck.Checked += (_, _) => SaveIfReady(() => _settings.AutoCopyToClipboard = true);
        AutoCopyCheck.Unchecked += (_, _) => SaveIfReady(() => _settings.AutoCopyToClipboard = false);

        AutoOpenEditorCheck.Checked += (_, _) => SaveIfReady(() => _settings.AutoOpenEditor = true);
        AutoOpenEditorCheck.Unchecked += (_, _) => SaveIfReady(() => _settings.AutoOpenEditor = false);

        PlaySoundCheck.Checked += (_, _) => SaveIfReady(() => _settings.PlayCaptureSound = true);
        PlaySoundCheck.Unchecked += (_, _) => SaveIfReady(() => _settings.PlayCaptureSound = false);

        StartWithWindowsCheck.Checked += (_, _) => SaveIfReady(() =>
        {
            _settings.StartWithWindows = true;
            StartupService.Register();
        });
        StartWithWindowsCheck.Unchecked += (_, _) => SaveIfReady(() =>
        {
            _settings.StartWithWindows = false;
            StartupService.Unregister();
        });
    }

    private void SaveIfReady(Action apply)
    {
        if (_initializing) return;
        apply();
        SettingsService.Save(_settings);
    }

    private void OnBrowseFolderClick(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            SelectedPath = Directory.Exists(_settings.SaveFolder) ? _settings.SaveFolder : string.Empty,
            Description = Loc.T("settings.chooseFolderDialogTitle")
        };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            _settings.SaveFolder = dialog.SelectedPath;
            SaveFolderBox.Text = dialog.SelectedPath;
            SettingsService.Save(_settings);
        }
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
