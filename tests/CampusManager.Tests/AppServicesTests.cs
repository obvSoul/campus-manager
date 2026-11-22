using CampusManager.App.Services;
using CampusManager.Core.Sync;

namespace CampusManager.Tests;

public sealed class AppServicesTests : IDisposable
{
    readonly TempDir _t = new();
    public void Dispose() => _t.Dispose();

    sealed class MemTokens : ITokenStore
    {
        public string? Token;
        public string? Load() => Token;
        public void Save(string token) => Token = token;
        public void Clear() => Token = null;
    }

    sealed class ThrowingApi(MoodleException e) : IMoodleApi
    {
        public Task<List<MoodleCourse>> CoursesAsync() => throw e;
        public Task<List<CourseSection>> ContentsAsync(long courseId) => throw e;
        public Task<List<Assignment>> AssignmentsAsync(IReadOnlyList<long> courseIds) => throw e;
        public Task<byte[]> DownloadAsync(string fileurl) => throw e;
        public Task<string?> SubmissionStatusAsync(long assignId) => throw e;
        public Task<List<ActionEvent>> ActionEventsAsync(long fromTime) => throw e;
    }

    AppServices Make(ITokenStore tokens, Func<Task<string>> login, Func<string, IMoodleApi> client, Func<string, Task<bool>>? check = null) =>
        new(_t.Combine("data"), _t.Combine("lib"), tokens, login, client, check ?? (_ => Task.FromResult(true)));

    [Fact]
    public void TokenRoundTrip_FileIsNotPlainText()
    {
        var s = new TokenStore(_t.Path);
        s.Save("SECRET123");
        Assert.Equal("SECRET123", new TokenStore(_t.Path).Load());
        Assert.DoesNotContain("SECRET123", File.ReadAllText(_t.Combine("token.bin")));
        s.Clear();
        Assert.Null(s.Load());
    }

    [Fact]
    public void AutostartCommandQuotesPathAndAddsHidden() =>
        Assert.Equal("\"C:\\Program Files\\Campus Manager\\Campus Manager.exe\" --hidden",
            Autostart.Command(@"C:\Program Files\Campus Manager\Campus Manager.exe"));

    [Fact]
    public async Task SyncOffline_SetsOfflineMessage()
    {
        using var s = Make(new MemTokens { Token = "T" }, () => Task.FromResult("T"), _ => new ThrowingApi(new("coupé", "network")),
            _ => throw new MoodleException("coupé", "network"));
        var st = await s.SyncAsync(false);
        Assert.False(st.Running);
        Assert.StartsWith("Hors ligne", st.Message);
    }

    [Fact]
    public async Task SyncAuthError_ClearsTokenAndCallsLogin()
    {
        var tokens = new MemTokens { Token = "OLD" };
        var logins = 0;
        using var s = Make(tokens, () => { logins++; return Task.FromResult("NEW"); }, _ => new FakeMoodle(),
            t => t == "OLD" ? throw new MoodleException("jeton", "invalidtoken") : Task.FromResult(true));
        await s.SyncAsync(false);
        Assert.Equal(1, logins);
        Assert.Equal("NEW", tokens.Token);
    }

    [Fact]
    public async Task LogNeverContainsToken()
    {
        using var s = Make(new MemTokens(), () => Task.FromResult("SECRET123"), _ => new FakeMoodle());
        var st = await s.SyncAsync(false);
        Assert.False(st.NeedsLogin);
        var log = File.ReadAllText(_t.Combine("data", "logs", "campus.log"));
        Assert.Contains("sync :", log);
        Assert.DoesNotContain("SECRET123", log);
    }
}
