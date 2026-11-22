using System.Diagnostics;
using Microsoft.VisualBasic.FileIO;

namespace CampusManager.App.Services;

/// <summary>Actions Windows sur les vrais fichiers.</summary>
public static class Shell
{
    /// <summary>Ouvre avec le logiciel associé. Retourne un message d'erreur, ou null.</summary>
    public static string? Open(string path)
    {
        try
        {
            if (!File.Exists(path) && !Directory.Exists(path)) return "fichier introuvable";
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return null;
        }
        catch (Exception e) { return e.Message; }
    }

    public static void Reveal(string path)
    {
        try { Process.Start("explorer.exe", File.Exists(path) ? $"/select,\"{path}\"" : $"\"{Path.GetDirectoryName(path)}\""); }
        catch { /* Explorateur indisponible */ }
    }

    public static void Trash(string path) =>
        FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
}
