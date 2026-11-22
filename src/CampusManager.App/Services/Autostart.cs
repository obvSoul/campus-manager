using Microsoft.Win32;

namespace CampusManager.App.Services;

/// <summary>
/// Démarrage avec Windows : valeur « Campus Manager » dans HKCU\…\Run (pas de droits administrateur).
/// Au démarrage du PC, l'appli se lance avec --hidden : seul le widget apparaît.
/// </summary>
public static class Autostart
{
    public const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = "Campus Manager";

    static string ExePath => Environment.ProcessPath!;
    public static string Command(string exePath) => $"\"{exePath}\" --hidden";

    public static bool Get()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue(ValueName) is string v && v.Length > 0;
    }

    public static bool Set(bool on)
    {
        try
        {
            using var k = Registry.CurrentUser.CreateSubKey(RunKey);
            if (on) k.SetValue(ValueName, Command(ExePath));
            else k.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch { /* registre inaccessible : l'état réel est relu ci-dessous */ }
        return Get();
    }

    /// <summary>
    /// Remet l'entrée en accord avec le choix de l'utilisateur : elle peut manquer (une réinstallation passe
    /// par le désinstalleur, qui la retire) ou pointer ailleurs (ancienne version).
    /// </summary>
    public static void Ensure(bool wanted)
    {
        if (!wanted) return;
        using var k = Registry.CurrentUser.OpenSubKey(RunKey);
        var current = k?.GetValue(ValueName) as string ?? "";
        if (!current.Contains(ExePath, StringComparison.OrdinalIgnoreCase)) Set(true);
    }
}
