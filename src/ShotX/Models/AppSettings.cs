using System.Windows.Input;

namespace ShotX.Models;

public enum AppTheme { System, Light, Dark }

public class HotkeyBinding
{
    public ModifierKeys Modifiers { get; set; }
    public Key Key { get; set; }

    public HotkeyBinding() { }

    public HotkeyBinding(ModifierKeys modifiers, Key key)
    {
        Modifiers = modifiers;
        Key = key;
    }

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
        var printScreenLabel = ShotX.Services.Loc.CurrentLanguage == "en" ? "Print Screen" : "Impr Pant";
        parts.Add(Key == Key.PrintScreen ? printScreenLabel : Key.ToString());
        return string.Join("+", parts);
    }
}

public class AppSettings
{
    public AppTheme Theme { get; set; } = AppTheme.System;
    public string Language { get; set; } = "es"; // "es" or "en"
    public string SaveFolder { get; set; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "ShotX");
    public string ImageFormat { get; set; } = "png"; // png or jpg
    public bool AutoCopyToClipboard { get; set; } = true;
    public bool AutoOpenEditor { get; set; } = true;
    public bool PlayCaptureSound { get; set; } = true;
    public bool StartWithWindows { get; set; } = true;
    public bool AutoUpdate { get; set; } = true;

    public HotkeyBinding RegionHotkey { get; set; } = new(ModifierKeys.None, Key.PrintScreen);
    public HotkeyBinding FullScreenHotkey { get; set; } = new(ModifierKeys.Control, Key.PrintScreen);
    public HotkeyBinding WindowHotkey { get; set; } = new(ModifierKeys.Alt, Key.PrintScreen);

    public int LastStepNumber { get; set; } = 0;

    // Remembered drawing defaults, so a choice made on one screenshot carries over to the next.
    public string LastColor { get; set; } = "#FFFF3B30";
    public double LastThickness { get; set; } = 4;
    public double LastFontSize { get; set; } = 20;
    public string LastFillBaseColor { get; set; } = "#FF000000";
    public int LastFillOpacity { get; set; } = 0; // 0-100, 0 = no fill
    public string LastTextHAlign { get; set; } = "Center"; // TextHAlign enum name
    public string LastTextVAlign { get; set; } = "Center"; // TextVAlign enum name
}
