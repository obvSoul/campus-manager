using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace CampusManager.App.Services;

/// <summary>Barre de titre Windows assortie au thème (attributs DWM 20 = mode sombre, 35 = couleur, 36 = texte).</summary>
public static class Dwm
{
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    static int Rgb(byte r, byte g, byte b) => r | (g << 8) | (b << 16);

    public static void DarkTitleBar(Window w, bool dark)
    {
        var h = new WindowInteropHelper(w).Handle;
        if (h == IntPtr.Zero) return;
        var on = dark ? 1 : 0;
        DwmSetWindowAttribute(h, 20, ref on, 4);
        var caption = dark ? Rgb(0x07, 0x07, 0x09) : Rgb(0xf6, 0xf7, 0xfa);
        var text = dark ? Rgb(0xdf, 0xe0, 0xe3) : Rgb(0x1d, 0x21, 0x30);
        DwmSetWindowAttribute(h, 35, ref caption, 4);
        DwmSetWindowAttribute(h, 36, ref text, 4);
    }
}
