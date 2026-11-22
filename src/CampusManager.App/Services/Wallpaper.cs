using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace CampusManager.App.Services;

/// <summary>
/// Fond d'écran Windows, chargé en PETITE taille (il est flouté de toute façon) : ~1 Mo en mémoire au lieu
/// de ~30 Mo pour une image 4K décodée en pleine résolution.
/// </summary>
public static class Wallpaper
{
    public static ImageSource? Load(int width = 480)
    {
        foreach (var path in Candidates())
        {
            try
            {
                if (!File.Exists(path)) continue;
                var bytes = File.ReadAllBytes(path); // le fichier n'est pas verrouillé
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = width;
                bmp.StreamSource = new MemoryStream(bytes);
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch { /* format non reconnu : candidat suivant */ }
        }
        return null;
    }

    static IEnumerable<string> Candidates()
    {
        using (var k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop"))
            if (k?.GetValue("WallPaper") is string p && p.Length > 0) yield return p;
        // Copie que Windows garde du fond d'écran (aussi pour un diaporama ou « Windows à la une »).
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Themes\TranscodedWallpaper");
    }
}
