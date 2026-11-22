using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CampusManager.Core.Sync;

/// <summary>
/// Client de l'API officielle Moodle (services web « moodle_mobile_app », la même que l'appli mobile).
/// C'est le SEUL code qui parle à EPREL avec le moteur de sync.
/// </summary>
public sealed class MoodleClient : IMoodleApi
{
    static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    readonly string _base;
    readonly string _token;
    readonly HttpClient _http;
    readonly TimeSpan _timeout;
    long? _userid;

    public MoodleClient(string baseUrl, string token, HttpMessageHandler? handler = null, TimeSpan? timeout = null)
    {
        _base = baseUrl.TrimEnd('/');
        _token = token;
        _timeout = timeout ?? TimeSpan.FromSeconds(30);
        _http = new HttpClient(handler ?? new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All }) { Timeout = Timeout.InfiniteTimeSpan };
    }

    string Hide(string s) => s.Replace(_token, "***");

    async Task<HttpResponseMessage> SendAsync(HttpRequestMessage req, TimeSpan timeout, string what)
    {
        using var cts = new CancellationTokenSource(timeout);
        HttpResponseMessage res;
        try { res = await _http.SendAsync(req, cts.Token); }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException)
        { throw new MoodleException(Hide($"{what} : {e.Message}"), "network"); }
        if ((int)res.StatusCode >= 500) throw new MoodleException($"Serveur EPREL en erreur ({(int)res.StatusCode})", "http5xx");
        return res;
    }

    static MoodleException? ErrorOf(JsonElement d)
    {
        if (d.ValueKind != JsonValueKind.Object) return null;
        if (!d.TryGetProperty("exception", out _) && !d.TryGetProperty("error", out _)) return null;
        string? S(string k) => d.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        return new MoodleException(S("message") ?? S("error") ?? "Erreur Moodle", S("errorcode") ?? "moodle");
    }

    async Task<T> CallAsync<T>(string fn, params (string Key, object Value)[] ps)
    {
        var form = new List<KeyValuePair<string, string>> { new("wstoken", _token), new("wsfunction", fn), new("moodlewsrestformat", "json") };
        foreach (var (k, v) in ps)
        {
            if (v is IEnumerable<long> list) form.AddRange(list.Select((x, i) => new KeyValuePair<string, string>($"{k}[{i}]", x.ToString())));
            else form.Add(new(k, Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture)!));
        }
        using var req = new HttpRequestMessage(HttpMethod.Post, $"{_base}/webservice/rest/server.php") { Content = new FormUrlEncodedContent(form) };
        using var res = await SendAsync(req, _timeout, "Réseau indisponible");
        if (!res.IsSuccessStatusCode) throw new MoodleException($"Réponse HTTP {(int)res.StatusCode}", "http");
        JsonElement data;
        try { data = JsonSerializer.Deserialize<JsonElement>(await res.Content.ReadAsStringAsync()); }
        catch (JsonException) { throw new MoodleException("Réponse non JSON (service désactivé ?)", "badresponse"); }
        if (ErrorOf(data) is { } err) throw err;
        return data.Deserialize<T>(Json) ?? throw new MoodleException("Réponse vide", "badresponse");
    }

    public Task<SiteInfo> SiteInfoAsync() => CallAsync<SiteInfo>("core_webservice_get_site_info");

    public async Task<List<MoodleCourse>> CoursesAsync()
    {
        _userid ??= (await SiteInfoAsync()).Userid;
        return await CallAsync<List<MoodleCourse>>("core_enrol_get_users_courses", ("userid", _userid));
    }

    public Task<List<CourseSection>> ContentsAsync(long courseId) => CallAsync<List<CourseSection>>("core_course_get_contents", ("courseid", courseId));

    record AssignCourse(long Id, List<Assignment> Assignments);
    record AssignResponse(List<AssignCourse> Courses);

    public async Task<List<Assignment>> AssignmentsAsync(IReadOnlyList<long> courseIds)
    {
        if (courseIds.Count == 0) return [];
        var r = await CallAsync<AssignResponse>("mod_assign_get_assignments", ("courseids", courseIds));
        return r.Courses.SelectMany(c => c.Assignments.Select(a => a with { Course = c.Id })).ToList();
    }

    record Sub(string? Status);
    record Attempt(Sub? Submission, Sub? Teamsubmission);
    record SubResponse(Attempt? Lastattempt);

    public async Task<string?> SubmissionStatusAsync(long assignId)
    {
        var r = await CallAsync<SubResponse>("mod_assign_get_submission_status", ("assignid", assignId));
        return r.Lastattempt?.Submission?.Status ?? r.Lastattempt?.Teamsubmission?.Status;
    }

    record Ev(long Id, string Modulename, long Instance, long Timesort, string? Url);
    record EvResponse(List<Ev> Events);

    public async Task<List<ActionEvent>> ActionEventsAsync(long fromTime)
    {
        var outList = new List<ActionEvent>();
        long? after = null;
        for (var page = 0; page < 10; page++) // pagination de la chronologie
        {
            var ps = new List<(string, object)> { ("timesortfrom", fromTime), ("limitnum", 50) };
            if (after != null) ps.Add(("aftereventid", after));
            var r = await CallAsync<EvResponse>("core_calendar_get_action_events_by_timesort", [.. ps]);
            outList.AddRange(r.Events.Select(e => new ActionEvent(e.Modulename, e.Instance, e.Timesort, e.Url)));
            if (r.Events.Count < 50) break;
            after = r.Events[^1].Id;
        }
        return outList;
    }

    /// <summary>Télécharge un fichier de cours. Le jeton n'apparaît dans aucun message d'erreur.</summary>
    public async Task<byte[]> DownloadAsync(string fileurl)
    {
        // Avec les services web, Moodle donne déjà des liens en /webservice/pluginfile.php ; on ne double pas.
        var url = fileurl.Contains("/webservice/pluginfile.php") ? fileurl : fileurl.Replace("/pluginfile.php", "/webservice/pluginfile.php");
        url += (url.Contains('?') ? "&" : "?") + "token=" + Uri.EscapeDataString(_token);
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        using var res = await SendAsync(req, _timeout * 4, "Téléchargement impossible");
        if (!res.IsSuccessStatusCode) throw new MoodleException($"Téléchargement refusé (HTTP {(int)res.StatusCode})", "http");
        var buf = await res.Content.ReadAsByteArrayAsync();
        if (res.Content.Headers.ContentType?.MediaType?.Contains("application/json") == true)
        {
            // Moodle répond en JSON quand le jeton n'a pas le droit de lire le fichier.
            var d = JsonSerializer.Deserialize<JsonElement>(buf);
            throw ErrorOf(d) ?? new MoodleException("Accès au fichier refusé", "accessexception");
        }
        return buf;
    }
}
