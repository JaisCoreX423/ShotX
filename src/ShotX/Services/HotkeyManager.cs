using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using ShotX.Models;

namespace ShotX.Services;

/// <summary>Registers system-wide hotkeys via a hidden message-only window.</summary>
public class HotkeyManager : IDisposable
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8, MOD_NOREPEAT = 0x4000;

    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _handlers = new();
    private int _nextId = 1;

    public HotkeyManager()
    {
        var parameters = new HwndSourceParameters("ShotXHotkeyWindow")
        {
            WindowStyle = 0,
            Width = 0,
            Height = 0,
            ParentWindow = new IntPtr(-3) // HWND_MESSAGE
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    public bool Register(HotkeyBinding binding, Action callback)
    {
        if (binding.Key == Key.None) return false;

        var id = _nextId++;
        var vk = (uint)KeyInterop.VirtualKeyFromKey(binding.Key);
        var modifiers = ToWin32Modifiers(binding.Modifiers) | MOD_NOREPEAT;

        var ok = RegisterHotKey(_source.Handle, id, modifiers, vk);
        if (ok)
        {
            _handlers[id] = callback;
            Logger.Log($"HotkeyManager: registered {binding} as id {id}.");
        }
        else
        {
            var error = Marshal.GetLastWin32Error();
            Logger.Log($"HotkeyManager: failed to register {binding} (likely already in use by another app, Win32 error {error}).");
        }
        return ok;
    }

    public void UnregisterAll()
    {
        foreach (var id in _handlers.Keys)
        {
            UnregisterHotKey(_source.Handle, id);
        }
        Logger.Log($"HotkeyManager: unregistered {_handlers.Count} hotkey(s).");
        _handlers.Clear();
    }

    private static uint ToWin32Modifiers(ModifierKeys modifiers)
    {
        uint result = 0;
        if (modifiers.HasFlag(ModifierKeys.Alt)) result |= MOD_ALT;
        if (modifiers.HasFlag(ModifierKeys.Control)) result |= MOD_CONTROL;
        if (modifiers.HasFlag(ModifierKeys.Shift)) result |= MOD_SHIFT;
        if (modifiers.HasFlag(ModifierKeys.Windows)) result |= MOD_WIN;
        return result;
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY)
        {
            var id = wParam.ToInt32();
            if (_handlers.TryGetValue(id, out var callback))
            {
                Logger.Log($"HotkeyManager: WM_HOTKEY received for id {id}, invoking callback.");
                handled = true;
                callback();
            }
            else
            {
                Logger.Log($"HotkeyManager: WM_HOTKEY received for unknown id {id} (no callback registered).");
            }
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        UnregisterAll();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
