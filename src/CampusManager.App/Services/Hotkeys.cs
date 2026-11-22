using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace CampusManager.App.Services;

/// <summary>Raccourcis globaux (fonctionnent même quand l'appli n'a pas le focus).</summary>
public sealed class Hotkeys : IDisposable
{
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    const uint ModAlt = 1, ModCtrl = 2, ModNoRepeat = 0x4000;
    const int WmHotkey = 0x0312;

    readonly HwndSource _src;
    readonly Dictionary<int, Action> _actions = [];

    public Hotkeys()
    {
        // Fenêtre de messages invisible, propre aux raccourcis.
        _src = new HwndSource(new HwndSourceParameters("CampusManager.Hotkeys") { ParentWindow = new IntPtr(-3) });
        _src.AddHook(Hook);
    }

    /// <summary>Ctrl+Alt+<paramref name="key"/> (lettre majuscule).</summary>
    public bool Register(char key, Action action)
    {
        var id = _actions.Count + 1;
        if (!RegisterHotKey(_src.Handle, id, ModCtrl | ModAlt | ModNoRepeat, key)) return false;
        _actions[id] = action;
        return true;
    }

    IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotkey && _actions.TryGetValue(wParam.ToInt32(), out var a)) { a(); handled = true; }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (var id in _actions.Keys) UnregisterHotKey(_src.Handle, id);
        _src.Dispose();
    }
}
