using System.Drawing;
using System.Windows.Forms;

namespace ShotX.Services;

/// <summary>Simple Greenshot-style tray icon: left-click opens the editor, right-click shows the menu.</summary>
public class TrayIconManager : IDisposable
{
    private readonly NotifyIcon _icon;

    public event Action? CaptureRegionRequested;
    public event Action? CaptureFullScreenRequested;
    public event Action? CaptureWindowRequested;
    public event Action? OpenEditorRequested;
    public event Action? OpenFolderRequested;
    public event Action? SettingsRequested;
    public event Action? ExitRequested;

    public TrayIconManager(Icon icon)
    {
        _icon = new NotifyIcon
        {
            Icon = icon,
            Visible = true,
            Text = "ShotX"
        };

        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) OpenEditorRequested?.Invoke();
        };

        _icon.ContextMenuStrip = BuildMenu();
        Loc.LanguageChanged += () => _icon.ContextMenuStrip = BuildMenu();
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip { Renderer = new ToolStripProfessionalRenderer(new ShotXColorTable()) };

        var captureRegion = new ToolStripMenuItem(Loc.T("tray.captureRegion"), null, (_, _) => CaptureRegionRequested?.Invoke());
        var captureFull = new ToolStripMenuItem(Loc.T("tray.captureFullScreen"), null, (_, _) => CaptureFullScreenRequested?.Invoke());
        var captureWindow = new ToolStripMenuItem(Loc.T("tray.captureWindow"), null, (_, _) => CaptureWindowRequested?.Invoke());
        var openEditor = new ToolStripMenuItem(Loc.T("tray.openEditor"), null, (_, _) => OpenEditorRequested?.Invoke());
        var openFolder = new ToolStripMenuItem(Loc.T("tray.openFolder"), null, (_, _) => OpenFolderRequested?.Invoke());
        var settings = new ToolStripMenuItem(Loc.T("tray.settings"), null, (_, _) => SettingsRequested?.Invoke());
        var exit = new ToolStripMenuItem(Loc.T("tray.exit"), null, (_, _) => ExitRequested?.Invoke());

        menu.Items.Add(captureRegion);
        menu.Items.Add(captureFull);
        menu.Items.Add(captureWindow);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(openEditor);
        menu.Items.Add(openFolder);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(settings);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exit);

        return menu;
    }

    public void ShowBalloon(string title, string text)
    {
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = text;
        _icon.ShowBalloonTip(2500);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}

internal class ShotXColorTable : ProfessionalColorTable
{
    private static readonly Color Accent = Color.FromArgb(10, 132, 255);

    public override Color MenuItemSelected => Color.FromArgb(235, 244, 255);
    public override Color MenuItemSelectedGradientBegin => Color.FromArgb(235, 244, 255);
    public override Color MenuItemSelectedGradientEnd => Color.FromArgb(235, 244, 255);
    public override Color MenuItemBorder => Accent;
    public override Color MenuBorder => Color.FromArgb(226, 226, 230);
    public override Color ToolStripDropDownBackground => Color.White;
    public override Color ImageMarginGradientBegin => Color.White;
    public override Color ImageMarginGradientMiddle => Color.White;
    public override Color ImageMarginGradientEnd => Color.White;
}
