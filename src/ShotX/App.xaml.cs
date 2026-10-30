using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ShotX.Models;
using ShotX.Services;
using ShotX.Views;
using Application = System.Windows.Application;

namespace ShotX;

public partial class App : Application
{
    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);

    private const int AsfwAny = -1;

    private Mutex? _singleInstanceMutex;
    private HotkeyManager? _hotkeyManager;
    private TrayIconManager? _trayIconManager;
    private CaptureController? _captureController;
    private MainWindow? _mainWindow;
    private EventWaitHandle? _showSignal;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += (_, ex) =>
        {
            Logger.Log($"UNHANDLED (Dispatcher): {ex.Exception}");
            ex.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) =>
        {
            Logger.Log($"UNHANDLED (AppDomain): {ex.ExceptionObject}");
        };

        var testSuffix = Environment.GetEnvironmentVariable("SHOTX_TEST_SUFFIX") ?? "";
        var showSignalName = $"Local\\ShotX.ShowSignal{testSuffix}";
        _singleInstanceMutex = new Mutex(true, $"ShotX.SingleInstance{testSuffix}", out var createdNew);
        if (!createdNew)
        {
            Logger.Log("App.OnStartup: another instance is already running; signaling it to show its window.");
            try
            {
                using var existingSignal = EventWaitHandle.OpenExisting(showSignalName);
                existingSignal.Set();
            }
            catch (Exception ex)
            {
                Logger.Log($"App.OnStartup: could not signal running instance: {ex.Message}");
            }
            Shutdown();
            return;
        }

        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, showSignalName);
        ThreadPool.RegisterWaitForSingleObject(_showSignal, (_, _) =>
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                AllowSetForegroundWindow(AsfwAny);
                ShowMainWindow();
            }));
        }, null, Timeout.Infinite, false);

        PathService.EnsureDataRoot();
        Logger.Log("App.OnStartup: starting.");

        var settings = SettingsService.Current;
        ThemeService.Apply(settings.Theme);
        ThemeService.StartListeningForSystemThemeChanges();
        ThemeService.SystemThemeChanged += () =>
        {
            if (SettingsService.Current.Theme == AppTheme.System) ThemeService.Apply(AppTheme.System);
        };

        if (HandleTestFlags(e.Args)) return;

        _captureController = new CaptureController
        {
            HideMainWindowIfVisible = () =>
            {
                if (_mainWindow is not { IsVisible: true }) return false;
                _mainWindow.Hide();
                return true;
            },
            ConfigureNewEditorWindow = editor => editor.SettingsRequested += OpenSettings
        };
        _hotkeyManager = new HotkeyManager();
        RegisterHotkeys(settings);

        var icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!)
                   ?? System.Drawing.SystemIcons.Application;
        _trayIconManager = new TrayIconManager(icon);
        _trayIconManager.CaptureRegionRequested += () => _captureController.CaptureRegion();
        _trayIconManager.CaptureFullScreenRequested += () => _captureController.CaptureFullScreen();
        _trayIconManager.CaptureWindowRequested += () => _captureController.CaptureActiveWindow();
        _trayIconManager.OpenEditorRequested += ShowMainWindow;
        _trayIconManager.OpenFolderRequested += OpenScreenshotsFolder;
        _trayIconManager.SettingsRequested += OpenSettings;
        _trayIconManager.ExitRequested += () =>
        {
            if (_mainWindow != null) _mainWindow.ForceClose = true;
            Shutdown();
        };

        if (settings.StartWithWindows && !StartupService.IsRegistered())
        {
            StartupService.Register();
        }

        UpdateService.Instance.IsIdle = () => _mainWindow is not { IsVisible: true };
        UpdateService.Instance.RequestShutdown = () => Dispatcher.BeginInvoke(new Action(Shutdown));
        UpdateService.Instance.Start();

        if (!e.Args.Contains("--background"))
        {
            ShowMainWindow();
        }

        Logger.Log("App.OnStartup: ready, tray icon visible.");
    }

    private void ShowMainWindow()
    {
        if (_mainWindow == null)
        {
            _mainWindow = new MainWindow();
            _mainWindow.CaptureRegionRequested += () => _captureController?.CaptureRegion();
            _mainWindow.CaptureFullScreenRequested += () => _captureController?.CaptureFullScreen();
            _mainWindow.CaptureWindowRequested += () => _captureController?.CaptureActiveWindow();
            _mainWindow.OpenImageRequested += OpenImageFile;
            _mainWindow.OpenFolderRequested += OpenScreenshotsFolder;
            _mainWindow.SettingsRequested += OpenSettings;
        }
        _mainWindow.RefreshHotkeyLabels();
        _mainWindow.ShowAndActivate();
    }

    private void RegisterHotkeys(AppSettings settings)
    {
        Logger.Log($"RegisterHotkeys: region={settings.RegionHotkey}, fullScreen={settings.FullScreenHotkey}, window={settings.WindowHotkey}");
        _hotkeyManager!.Register(settings.RegionHotkey, () =>
        {
            Logger.Log("Hotkey fired: CaptureRegion");
            _captureController!.CaptureRegion();
        });
        _hotkeyManager.Register(settings.FullScreenHotkey, () =>
        {
            Logger.Log("Hotkey fired: CaptureFullScreen");
            _captureController!.CaptureFullScreen();
        });
        _hotkeyManager.Register(settings.WindowHotkey, () =>
        {
            Logger.Log("Hotkey fired: CaptureActiveWindow");
            _captureController!.CaptureActiveWindow();
        });
    }

    private void OpenEditorForLastOrPrompt()
    {
        if (_captureController?.LastCapture is { } last)
        {
            var existingEditor = EditorWindow.ForNewCapture(last);
            existingEditor.SettingsRequested += OpenSettings;
            existingEditor.Show();
            return;
        }

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Imágenes|*.png;*.jpg;*.jpeg;*.bmp",
            InitialDirectory = System.IO.Directory.Exists(SettingsService.Current.SaveFolder)
                ? SettingsService.Current.SaveFolder
                : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
        };
        if (dialog.ShowDialog() == true)
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(dialog.FileName);
            image.EndInit();
            image.Freeze();
            var newEditor = new EditorWindow(image);
            newEditor.SettingsRequested += OpenSettings;
            newEditor.Show();
        }
    }

    private void OpenImageFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Imágenes|*.png;*.jpg;*.jpeg;*.bmp",
            InitialDirectory = System.IO.Directory.Exists(SettingsService.Current.SaveFolder)
                ? SettingsService.Current.SaveFolder
                : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)
        };
        if (dialog.ShowDialog() == true)
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(dialog.FileName);
            image.EndInit();
            image.Freeze();
            var importedEditor = new EditorWindow(image);
            importedEditor.SettingsRequested += OpenSettings;
            importedEditor.Show();
        }
    }

    private void OpenScreenshotsFolder()
    {
        try
        {
            var folder = SettingsService.Current.SaveFolder;
            System.IO.Directory.CreateDirectory(folder);
            System.Diagnostics.Process.Start("explorer.exe", folder);
        }
        catch (Exception ex)
        {
            Logger.Log($"App.OpenScreenshotsFolder failed: {ex.Message}");
        }
    }

    private SettingsWindow? _settingsWindow;

    private void OpenSettings()
    {
        if (_settingsWindow is { IsLoaded: true })
        {
            _settingsWindow.Activate();
            return;
        }
        _settingsWindow = new SettingsWindow();
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.HotkeysChanged += ReRegisterHotkeys;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void ReRegisterHotkeys()
    {
        Logger.Log("ReRegisterHotkeys: settings changed, re-registering.");
        _hotkeyManager?.UnregisterAll();
        if (_hotkeyManager != null) RegisterHotkeys(SettingsService.Current);
        _mainWindow?.RefreshHotkeyLabels();
    }

    private bool HandleTestFlags(string[] args)
    {
        if (args.Contains("--test-open-editor"))
        {
            var image = TestImageFactory.CreateSampleScreenshot();
            var editor = new EditorWindow(image, "Captura de prueba");
            editor.Show();
            editor.ContentRendered += (_, _) =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    DiagSnapshot.Capture(editor, "editor-window");
                    Shutdown();
                }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            };
            return true;
        }

        if (args.Contains("--test-text-tool"))
        {
            var image = TestImageFactory.CreateSampleScreenshot();
            var editor = new EditorWindow(image, "Captura de prueba");
            editor.Show();
            editor.SimulateTextToolClickForTesting(new System.Windows.Point(300, 220));
            editor.ContentRendered += (_, _) =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    DiagSnapshot.Capture(editor, "editor-window-texttool");
                    Shutdown();
                }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            };
            return true;
        }

        if (args.Contains("--test-image-import"))
        {
            var image = TestImageFactory.CreateSampleScreenshot();
            var editor = new EditorWindow(image, "Captura de prueba");
            editor.Show();
            var imported = TestImageFactory.CreateSampleScreenshot(640, 480);
            editor.SimulateImageImportForTesting(imported);
            editor.ContentRendered += (_, _) =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    DiagSnapshot.Capture(editor, "editor-window-imageimport");
                    Shutdown();
                }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            };
            return true;
        }

        if (args.Contains("--test-text-centered"))
        {
            var image = TestImageFactory.CreateSampleScreenshot();
            var editor = new EditorWindow(image, "Captura de prueba");
            editor.Show();
            editor.ContentRendered += (_, _) =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    editor.SimulateTextToolSelectForTesting();
                    editor.SimulateTextCommitAndResizeForTesting("Nota de prueba\nsegunda línea");
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        DiagSnapshot.Capture(editor, "editor-window-text-centered");
                        Shutdown();
                    }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            };
            return true;
        }

        if (args.Contains("--test-crop-live-preview"))
        {
            var image = TestImageFactory.CreateSampleScreenshot();
            var editor = new EditorWindow(image, "Captura de prueba");
            editor.Show();
            editor.SimulateCropDragInProgressForTesting();
            editor.ContentRendered += (_, _) =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    DiagSnapshot.Capture(editor, "editor-window-crop-live-preview");
                    Shutdown();
                }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            };
            return true;
        }

        if (args.Contains("--test-persisted-defaults"))
        {
            var image1 = TestImageFactory.CreateSampleScreenshot();
            var editor1 = new EditorWindow(image1, "Captura 1");
            editor1.Show();
            editor1.SimulateSetDrawingDefaultsForTesting();
            editor1.Close();

            var image2 = TestImageFactory.CreateSampleScreenshot();
            var editor2 = new EditorWindow(image2, "Captura 2 (debe heredar los valores)");
            editor2.Show();
            editor2.SimulateDrawRectangleForTesting();
            editor2.ContentRendered += (_, _) =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    DiagSnapshot.Capture(editor2, "editor-window-persisted-defaults");
                    Shutdown();
                }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            };
            return true;
        }

        if (args.Contains("--test-background-popup"))
        {
            var image = TestImageFactory.CreateSampleScreenshot();
            var editor = new EditorWindow(image, "Captura de prueba");
            editor.Show();
            editor.ContentRendered += (_, _) =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    editor.SimulateOpenBackgroundPopupForTesting();
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (editor.BackgroundPopupContentForTesting is { } popupContent)
                        {
                            DiagSnapshot.Capture(popupContent, "editor-window-background-popup");
                        }
                        Shutdown();
                    }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            };
            return true;
        }

        if (args.Contains("--test-color-apply"))
        {
            var image = TestImageFactory.CreateSampleScreenshot();
            var editor = new EditorWindow(image, "Captura de prueba");
            editor.Show();
            editor.AddSampleAnnotationsForTesting();
            editor.SimulateColorApplyToSelectionForTesting();
            editor.ContentRendered += (_, _) =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    DiagSnapshot.Capture(editor, "editor-window-color-apply");
                    Shutdown();
                }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            };
            return true;
        }

        if (args.Contains("--test-text-background"))
        {
            var image = TestImageFactory.CreateSampleScreenshot();
            var editor = new EditorWindow(image, "Captura de prueba");
            editor.Show();
            editor.AddSampleAnnotationsForTesting();
            editor.SimulateTextBackgroundForTesting();
            editor.ContentRendered += (_, _) =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    DiagSnapshot.Capture(editor, "editor-window-text-background");
                    Shutdown();
                }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            };
            return true;
        }

        if (args.Contains("--test-layer-reorder"))
        {
            var image = TestImageFactory.CreateSampleScreenshot();
            var editor = new EditorWindow(image, "Captura de prueba");
            editor.Show();
            editor.AddSampleAnnotationsForTesting();
            editor.SimulateLayerReorderForTesting();
            editor.ContentRendered += (_, _) =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    DiagSnapshot.Capture(editor, "editor-window-layer-reorder");
                    Shutdown();
                }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            };
            return true;
        }

        if (args.Contains("--test-text-stepper"))
        {
            var image = TestImageFactory.CreateSampleScreenshot();
            var editor = new EditorWindow(image, "Captura de prueba");
            editor.Show();
            editor.AddSampleAnnotationsForTesting();
            editor.SimulateTextSizeStepperForTesting();
            editor.ContentRendered += (_, _) =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    DiagSnapshot.Capture(editor, "editor-window-text-stepper");
                    Shutdown();
                }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            };
            return true;
        }

        if (args.Contains("--test-stepper-selection"))
        {
            var image = TestImageFactory.CreateSampleScreenshot();
            var editor = new EditorWindow(image, "Captura de prueba");
            editor.Show();
            editor.AddSampleAnnotationsForTesting();
            editor.SimulateSelectionStepperForTesting();
            editor.ContentRendered += (_, _) =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    DiagSnapshot.Capture(editor, "editor-window-stepper-selection");
                    Shutdown();
                }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            };
            return true;
        }

        if (args.Contains("--test-text-edit"))
        {
            var image = TestImageFactory.CreateSampleScreenshot();
            var editor = new EditorWindow(image, "Captura de prueba");
            editor.Show();
            editor.AddSampleAnnotationsForTesting();
            editor.SimulateDoubleClickEditForTesting();
            editor.ContentRendered += (_, _) =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    DiagSnapshot.Capture(editor, "editor-window-text-edit");
                    Shutdown();
                }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            };
            return true;
        }

        if (args.Contains("--test-shape-text"))
        {
            var image = TestImageFactory.CreateSampleScreenshot();
            var editor = new EditorWindow(image, "Captura de prueba");
            editor.Show();
            editor.AddSampleAnnotationsForTesting();
            editor.SimulateShapeTextEditForTesting("Paso 1");
            editor.ContentRendered += (_, _) =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    DiagSnapshot.Capture(editor, "editor-window-shape-text");
                    Shutdown();
                }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            };
            return true;
        }

        if (args.Contains("--test-text-align"))
        {
            var image = TestImageFactory.CreateSampleScreenshot();
            var editor = new EditorWindow(image, "Captura de prueba");
            editor.Show();
            editor.AddSampleAnnotationsForTesting();
            editor.SimulateShapeTextEditForTesting("Alineado");
            editor.SimulateTextAlignForTesting(TextHAlign.Left, TextVAlign.Top);
            editor.ContentRendered += (_, _) =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    DiagSnapshot.Capture(editor, "editor-window-text-align");
                    Shutdown();
                }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            };
            return true;
        }

        if (args.Contains("--test-duplicate-shortcut"))
        {
            var image = TestImageFactory.CreateSampleScreenshot();
            var editor = new EditorWindow(image, "Captura de prueba");
            editor.Show();
            editor.AddSampleAnnotationsForTesting();
            editor.SimulateDuplicateShortcutForTesting();
            editor.ContentRendered += (_, _) =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    DiagSnapshot.Capture(editor, "editor-window-duplicate-shortcut");
                    Shutdown();
                }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            };
            return true;
        }

        if (args.Contains("--test-copy-paste-shortcut"))
        {
            var image = TestImageFactory.CreateSampleScreenshot();
            var editor = new EditorWindow(image, "Captura de prueba");
            editor.Show();
            editor.AddSampleAnnotationsForTesting();
            editor.SimulateCopyPasteShortcutForTesting();
            editor.ContentRendered += (_, _) =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    DiagSnapshot.Capture(editor, "editor-window-copy-paste-shortcut");
                    Shutdown();
                }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            };
            return true;
        }

        if (args.Contains("--test-delete-shortcut"))
        {
            var image = TestImageFactory.CreateSampleScreenshot();
            var editor = new EditorWindow(image, "Captura de prueba");
            editor.Show();
            editor.AddSampleAnnotationsForTesting();
            editor.SimulateDeleteSelectedForTesting();
            editor.ContentRendered += (_, _) =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    DiagSnapshot.Capture(editor, "editor-window-delete-shortcut");
                    Shutdown();
                }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            };
            return true;
        }

        if (args.Contains("--test-maximized"))
        {
            var image = TestImageFactory.CreateSampleScreenshot();
            var editor = new EditorWindow(image, "Captura de prueba");
            editor.WindowState = WindowState.Maximized;
            editor.Show();
            editor.ContentRendered += (_, _) =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    DiagSnapshot.Capture(editor, "editor-window-maximized");
                    Shutdown();
                }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            };
            return true;
        }

        if (args.Contains("--test-annotations"))
        {
            var image = TestImageFactory.CreateSampleScreenshot();
            var editor = new EditorWindow(image, "Captura de prueba");
            editor.Show();
            editor.AddSampleAnnotationsForTesting();
            editor.ContentRendered += (_, _) =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    DiagSnapshot.Capture(editor, "editor-window-annotations");
                    Shutdown();
                }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            };
            return true;
        }

        if (args.Contains("--test-hide-before-capture"))
        {
            var main = new MainWindow();
            main.Left = 100;
            main.Top = 100;
            main.Show();

            var controller = new CaptureController
            {
                HideMainWindowIfVisible = () =>
                {
                    if (main is not { IsVisible: true }) return false;
                    main.Hide();
                    return true;
                }
            };

            Dispatcher.BeginInvoke(new Action(async () =>
            {
                await Task.Delay(800); // let MainWindow actually paint on screen first
                Logger.Log("test-hide-before-capture: MainWindow shown, capturing full screen now.");
                controller.CaptureFullScreen();
                await Task.Delay(1500); // CaptureFullScreen/Finish/editor open are not awaitable from here

                EditorWindow? editor = null;
                foreach (Window w in Windows)
                {
                    if (w is EditorWindow ew) { editor = ew; break; }
                }

                if (editor != null)
                {
                    DiagSnapshot.Capture(editor, "hide-before-capture-result");
                }
                else
                {
                    Logger.Log("test-hide-before-capture: no editor window found to snapshot.");
                }
                Shutdown();
            }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);

            return true;
        }

        if (args.Contains("--test-main-window"))
        {
            var main = new MainWindow();
            main.Show();
            main.ContentRendered += (_, _) =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    DiagSnapshot.Capture(main, "main-window");
                    Shutdown();
                }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            };
            return true;
        }

        if (args.Contains("--test-hotkey-rebind"))
        {
            var settingsWindow = new SettingsWindow();
            settingsWindow.Show();
            settingsWindow.SimulateHotkeyClickForTesting("region");
            settingsWindow.SimulateHotkeyCaptureForTesting(ModifierKeys.Control | ModifierKeys.Shift, Key.R);
            settingsWindow.ContentRendered += (_, _) =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    DiagSnapshot.Capture(settingsWindow, "settings-hotkey-rebind");
                    Shutdown();
                }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            };
            return true;
        }

        if (args.Contains("--test-hotkey-duplicate"))
        {
            var settingsWindow = new SettingsWindow();
            settingsWindow.Show();
            settingsWindow.SimulateHotkeyClickForTesting("fullscreen");
            // Region's default is bare PrintScreen; try to rebind FullScreen to the same combo.
            settingsWindow.SimulateHotkeyCaptureForTesting(ModifierKeys.None, Key.PrintScreen);
            settingsWindow.ContentRendered += (_, _) =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    DiagSnapshot.Capture(settingsWindow, "settings-hotkey-duplicate");
                    Shutdown();
                }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            };
            return true;
        }

        if (args.Contains("--test-settings"))
        {
            var settingsWindow = new SettingsWindow();
            settingsWindow.Show();
            settingsWindow.ContentRendered += (_, _) =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    DiagSnapshot.Capture(settingsWindow, "settings-window");
                    Shutdown();
                }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            };
            return true;
        }

        if (args.Contains("--test-capture-overlay"))
        {
            var sample = TestImageFactory.CreateSampleScreenshot(1280, 800);
            var bitmap = ScreenCaptureService.FromBitmapSource(sample);
            var bounds = new System.Drawing.Rectangle(0, 0, 1280, 800);
            var overlay = new CaptureOverlayWindow(bitmap, bounds, 1.0);
            overlay.Show();
            overlay.ContentRendered += (_, _) =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    overlay.Overlay.SetHole(new Rect(200, 150, 400, 250), isSuggestion: false);
                    overlay.Overlay.UpdateLayout();
                    Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Render);
                    DiagSnapshot.Capture(overlay, "capture-overlay");
                    Shutdown();
                }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            };
            return true;
        }

        if (args.Contains("--test-dark"))
        {
            SettingsService.Current.Theme = AppTheme.Dark;
            ThemeService.Apply(AppTheme.Dark);
            var image = TestImageFactory.CreateSampleScreenshot();
            var editor = new EditorWindow(image, "Captura de prueba (oscuro)");
            editor.Show();
            editor.ContentRendered += (_, _) =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    DiagSnapshot.Capture(editor, "editor-window-dark");
                    Shutdown();
                }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            };
            return true;
        }

        return false;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Logger.Log("App.OnExit.");
        if (_mainWindow != null) _mainWindow.ForceClose = true;
        _hotkeyManager?.Dispose();
        _trayIconManager?.Dispose();
        _showSignal?.Dispose();
        _singleInstanceMutex?.ReleaseMutex();
        base.OnExit(e);
    }
}
