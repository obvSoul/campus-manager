using System.Security.Cryptography;
using System.Text;

namespace CampusManager.Core.Sync;

/// <summary>
/// Connexion SSO officielle de l'appli mobile Moodle. Le mot de passe est saisi sur la page UPEC, jamais lu
/// par nous ; on reçoit seulement <c>moodlemobile://token=BASE64(md5(site+passport):::jeton[:::privé])</c>
/// et on vérifie le md5 pour être sûr que le jeton répond à NOTRE demande.
/// </summary>
public static class Sso
{
    // EPREL n'accepte que le schéma officiel de l'appli mobile (un schéma personnalisé est ignoré).
    public const string UrlScheme = "moodlemobile";
    const string Prefix = UrlScheme + "://token=";

    static string Site(string baseUrl) => baseUrl.TrimEnd('/');

    public static string NewPassport() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(12));

    public static string BuildLaunchUrl(string baseUrl, string passport) =>
        $"{Site(baseUrl)}/admin/tool/mobile/launch.php?service=moodle_mobile_app&passport={Uri.EscapeDataString(passport)}&urlscheme={UrlScheme}";

    public static bool IsTokenRedirect(string url) => url.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

    public static (string Token, string? PrivateToken) ParseTokenUrl(string url, string baseUrl, string passport)
    {
        if (!IsTokenRedirect(url)) throw new MoodleException("Redirection inattendue", "badresponse");
        string decoded;
        try { decoded = Encoding.UTF8.GetString(Convert.FromBase64String(Uri.UnescapeDataString(url[Prefix.Length..]))); }
        catch (FormatException) { throw new MoodleException("Jeton illisible", "badresponse"); }
        var parts = decoded.Split(":::");
        var expected = Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(Site(baseUrl) + passport)));
        if (parts[0] != expected)
            throw new MoodleException("Jeton refusé : il ne correspond pas à cette demande de connexion", "badpassport");
        if (parts.Length < 2 || parts[1].Length == 0) throw new MoodleException("Jeton vide", "badresponse");
        return (parts[1], parts.Length > 2 ? parts[2] : null);
    }
}
