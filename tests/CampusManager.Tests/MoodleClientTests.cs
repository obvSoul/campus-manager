using System.Net;
using System.Text;
using CampusManager.Core.Sync;

namespace CampusManager.Tests;

public class MoodleClientTests
{
    /// <summary>Faux serveur HTTP : enregistre les requêtes et répond ce qu'on lui dit.</summary>
    sealed class Handler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public readonly List<(string Url, string Body)> Calls = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            Calls.Add((r.RequestUri!.ToString(), r.Content is null ? "" : await r.Content.ReadAsStringAsync(ct)));
            return await respond(r);
        }
    }
    static HttpResponseMessage Json(string s) => new(HttpStatusCode.OK) { Content = new StringContent(s, Encoding.UTF8, "application/json") };

    [Fact]
    public async Task TokenNeverInApiUrl_AuthErrorRecognized()
    {
        var h = new Handler(_ => Task.FromResult(Json("""{"exception":"x","errorcode":"invalidtoken","message":"Jeton invalide"}""")));
        var e = await Assert.ThrowsAsync<MoodleException>(() => new MoodleClient("https://e", "SECRET", h).SiteInfoAsync());
        Assert.True(e.IsAuthError);
        Assert.DoesNotContain("SECRET", h.Calls[0].Url);
        Assert.Contains("wstoken=SECRET", h.Calls[0].Body);
    }

    [Fact]
    public async Task DownloadRefusedAsJson_ClearError()
    {
        var h = new Handler(_ => Task.FromResult(Json("""{"error":"Accès refusé","errorcode":"accessexception"}""")));
        var e = await Assert.ThrowsAsync<MoodleException>(() => new MoodleClient("https://e", "SECRET", h).DownloadAsync("https://e/pluginfile.php/a.pdf"));
        Assert.True(e.IsAuthError);
        Assert.DoesNotContain("SECRET", e.Message);
    }

    [Fact]
    public async Task DownloadUrl_NoDoubledWebservice_TokenAsParameter()
    {
        var h = new Handler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("ok") }));
        var c = new MoodleClient("https://e", "TOK", h);
        await c.DownloadAsync("https://e/webservice/pluginfile.php/1/a.pdf?forcedownload=1");
        await c.DownloadAsync("https://e/pluginfile.php/1/b.pdf");
        Assert.Equal("https://e/webservice/pluginfile.php/1/a.pdf?forcedownload=1&token=TOK", h.Calls[0].Url);
        Assert.Equal("https://e/webservice/pluginfile.php/1/b.pdf?token=TOK", h.Calls[1].Url);
    }

    [Fact]
    public async Task NetworkDown_IsTransient_TokenNotLeaked()
    {
        var h = new Handler(r => throw new HttpRequestException($"échec {r.RequestUri}"));
        var e = await Assert.ThrowsAsync<MoodleException>(() => new MoodleClient("https://e", "SECRET", h).DownloadAsync("https://e/pluginfile.php/a.pdf"));
        Assert.True(e.IsTransient);
        Assert.DoesNotContain("SECRET", e.Message);
    }
}
