using System.Security.Cryptography;
using System.Text;

namespace CampusManager.App.Services;

public interface ITokenStore
{
    string? Load();
    void Save(string token);
    void Clear();
}

/// <summary>Jeton chiffré par Windows (DPAPI) : lisible seulement par ta session Windows.</summary>
public sealed class TokenStore(string dataDir) : ITokenStore
{
    static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CampusManager.token.v2");
    string File_ => Path.Combine(dataDir, "token.bin");

    public string? Load()
    {
        try { return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(File_), Entropy, DataProtectionScope.CurrentUser)); }
        catch { return null; } // absent, illisible ou ancien format Electron : reconnexion
    }

    public void Save(string token)
    {
        Directory.CreateDirectory(dataDir);
        File.WriteAllBytes(File_, ProtectedData.Protect(Encoding.UTF8.GetBytes(token), Entropy, DataProtectionScope.CurrentUser));
    }

    public void Clear() { try { File.Delete(File_); } catch { /* déjà absent */ } }
}
