using System.Security.Cryptography;
using System.Text;
using CampusManager.Core.Sync;

namespace CampusManager.Tests;

public class SsoTests
{
    const string Base = "https://eprel.example";
    static string Id(string p) => Convert.ToHexStringLower(MD5.HashData(Encoding.UTF8.GetBytes(Base + p)));
    static string Enc(string s) => $"moodlemobile://token={Convert.ToBase64String(Encoding.UTF8.GetBytes(s))}";

    [Fact]
    public void BuildsUrl_AndVerifiesReceivedToken()
    {
        var u = new Uri(Sso.BuildLaunchUrl(Base, "p"));
        Assert.Contains("passport=p", u.Query);
        Assert.StartsWith("https://eprel.example/admin/tool/mobile/launch.php?", u.ToString());
        Assert.Equal(("TOK", "PRIV"), Sso.ParseTokenUrl(Enc($"{Id("p")}:::TOK:::PRIV"), Base, "p"));
        Assert.NotEqual(Sso.NewPassport(), Sso.NewPassport());
    }

    [Fact]
    public void RejectsTokenFromAnotherRequestOrSite()
    {
        Assert.Throws<MoodleException>(() => Sso.ParseTokenUrl(Enc($"{Id("autre")}:::TOK"), Base, "p"));
        var e = Assert.Throws<MoodleException>(() => Sso.ParseTokenUrl(Enc($"{Id("p")}:::TOK"), "https://evil", "p"));
        Assert.Contains("ne correspond pas", e.Message);
    }
}
