using System.Runtime.InteropServices;
using System.Text;

namespace CampusManager.App.Widget;

/// <summary>
/// Appels Win32 du widget. Intégration au bureau : la fenêtre est « possédée » par la fenêtre du bureau
/// (Progman), comme les widgets Rainmeter en mode « sur le bureau » : elle reste au niveau du bureau,
/// sous les applications, visible avec Win+D, et cliquable. Tout échec est sans gravité.
/// </summary>
public static class Win32
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowW(string? cls, string? name);
    [DllImport("user32.dll")] static extern IntPtr SetWindowLongPtrW(IntPtr h, int idx, IntPtr v);
    [DllImport("user32.dll")] static extern IntPtr GetWindowLongPtrW(IntPtr h, int idx);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassNameW(IntPtr h, StringBuilder s, int max);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
    [DllImport("user32.dll")] static extern bool GetMonitorInfoW(IntPtr mon, ref MONITORINFO mi);

    [StructLayout(LayoutKind.Sequential)] struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { public int Size; public RECT Monitor, Work; public uint Flags; }

    const int GWLP_HWNDPARENT = -8, GWL_EXSTYLE = -20;
    const long WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_APPWINDOW = 0x40000, WS_EX_LAYERED = 0x80000;
    static readonly IntPtr HWND_BOTTOM = new(1);
    const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10;

    static void SetEx(IntPtr h, long add, long remove)
    {
        var ex = GetWindowLongPtrW(h, GWL_EXSTYLE).ToInt64();
        SetWindowLongPtrW(h, GWL_EXSTYLE, new IntPtr((ex | add) & ~remove));
    }

    /// <summary>Fenêtre « outil » : absente de la barre des tâches et d'Alt+Tab.</summary>
    public static void MakeToolWindow(IntPtr h) => SetEx(h, WS_EX_TOOLWINDOW, WS_EX_APPWINDOW);

    /// <summary>Rattache au bureau. Retourne false si l'intégration n'a pas pu se faire.</summary>
    public static bool AttachToDesktop(IntPtr h)
    {
        var progman = FindWindowW("Progman", null);
        if (progman == IntPtr.Zero) return false;
        SetWindowLongPtrW(h, GWLP_HWNDPARENT, progman);
        SendToBottom(h);
        return true;
    }

    /// <summary>Replace la fenêtre sous toutes les applications, sans l'activer.</summary>
    public static void SendToBottom(IntPtr h) => SetWindowPos(h, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOSIZE | SWP_NOMOVE | SWP_NOACTIVATE);

    /// <summary>Les clics traversent la fenêtre (vers le bureau ou l'application en dessous).</summary>
    public static void SetClickThrough(IntPtr h, bool on)
    {
        if (on) SetEx(h, WS_EX_TRANSPARENT | WS_EX_LAYERED, 0);
        else SetEx(h, 0, WS_EX_TRANSPARENT);
    }

    /// <summary>Une application (jeu, vidéo, présentation) occupe-t-elle tout l'écran ?</summary>
    public static bool IsForegroundFullscreen(IReadOnlyCollection<IntPtr> own)
    {
        var fg = GetForegroundWindow();
        if (fg == IntPtr.Zero || own.Contains(fg) || fg == GetShellWindow()) return false;
        var sb = new StringBuilder(64);
        GetClassNameW(fg, sb, 64);
        if (sb.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;
        if (!GetWindowRect(fg, out var r)) return false;
        var mi = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfoW(MonitorFromWindow(fg, 2), ref mi)) return false;
        return r.Left <= mi.Monitor.Left && r.Top <= mi.Monitor.Top && r.Right >= mi.Monitor.Right && r.Bottom >= mi.Monitor.Bottom;
    }
}
