using System.Text.Json;
using System.Windows.Threading;
using CampusManager.Core.Queries;

namespace CampusManager.App.Services;

/// <summary>Réglages des notifications Windows (clé <c>settings.notifications</c>).</summary>
public sealed class NotifySettings
{
    public bool Enabled { get; set; } = true;
    /// <summary>Nouveaux fichiers trouvés par une synchro automatique.</summary>
    public bool NewFiles { get; set; } = true;
    /// <summary>Rappel 24 h avant la date limite d'un devoir non terminé.</summary>
    public bool Deadlines { get; set; } = true;

    static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };
    public static NotifySettings FromJson(string? s) { try { return s == null ? new() : JsonSerializer.Deserialize<NotifySettings>(s, Json) ?? new(); } catch { return new(); } }
    public string ToJson() => JsonSerializer.Serialize(this, Json);
}

/// <summary>
/// Synchronisation automatique en arrière-plan + notifications Windows.
/// Une synchro automatique n'ouvre jamais la fenêtre de connexion : si la session a expiré, une notification le dit.
/// </summary>
public sealed class AutoSync
{
    public static readonly (int Minutes, string Label)[] Intervals =
    [
        (0, "Jamais"), (30, "Toutes les 30 minutes"), (60, "Toutes les heures"), (120, "Toutes les 2 heures"),
        (240, "Toutes les 4 heures"), (720, "Toutes les 12 heures"), (1440, "Une fois par jour"),
    ];
    public const int DefaultMinutes = 120;
    const int DeadlineHours = 24;

    // ---------- Logique pure (testée) ----------

    /// <summary>Faut-il synchroniser ? (dernière synchro ou tentative plus vieille que l'intervalle ; 0 = jamais)</summary>
    public static bool IsDue(long? last, int intervalMinutes, long now) =>
        intervalMinutes > 0 && (last == null || now - last.Value >= intervalMinutes * 60L);

    /// <summary>Devoirs non terminés dont la date limite tombe dans les 24 h, pas encore signalés.</summary>
    public static List<AssignmentItem> DueSoon(IEnumerable<AssignmentItem> list, long now, ISet<long> notified, int hours = DeadlineHours) =>
        list.Where(a => !a.Old && a.Status != "done" && a.DueAt is { } d && d > now && d - now <= hours * 3600L && !notified.Contains(a.Id)).ToList();

    /// <summary>« 3 nouveaux fichiers : R1.01 (2), R1.02 (1) » (matière abrégée à son code).</summary>
    public static string NewFilesMessage(IReadOnlyList<(string Course, int Count)> perCourse)
    {
        var total = perCourse.Sum(p => p.Count);
        static string Code(string c) => c.Split(" - ")[0];
        var detail = perCourse.Count == 1 && total == 1 ? Code(perCourse[0].Course) : string.Join(", ", perCourse.Select(p => $"{Code(p.Course)} ({p.Count})"));
        return $"{total} {(total > 1 ? "nouveaux fichiers" : "nouveau fichier")} : {detail}";
    }

    // ---------- Fonctionnement ----------

    readonly AppServices _s;
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMinutes(1) };
    long? _lastAttempt;
    bool _loginNotified;

    public AutoSync(AppServices s)
    {
        _s = s;
        _timer.Tick += async (_, _) => await TickAsync();
    }

    public int Minutes
    {
        get => int.TryParse(_s.Setting("autoSyncMinutes"), out var m) ? m : DefaultMinutes;
        set => _s.SetSetting("autoSyncMinutes", value.ToString());
    }

    public NotifySettings Notify
    {
        get => NotifySettings.FromJson(_s.Setting("notifications"));
        set => _s.SetSetting("notifications", value.ToJson());
    }

    public void Start() => _timer.Start();

    async Task TickAsync()
    {
        var now = Fmt.Now();
        // Pas plus d'une tentative par intervalle, même hors ligne.
        var last = Math.Max(_s.State.LastAt ?? 0, _lastAttempt ?? 0);
        if (!_s.State.Running && IsDue(last == 0 ? null : last, Minutes, now))
        {
            _lastAttempt = now;
            await RunAsync(now);
        }
        CheckDeadlines(now);
    }

    async Task RunAsync(long started)
    {
        var st = await _s.SyncAsync(relogin: false, interactive: false);
        var n = Notify;
        if (st.NeedsLogin)
        {
            if (n.Enabled && !_loginNotified)
                Notifier.Show("Connexion EPREL nécessaire", "La session a expiré : ouvre Campus Manager et synchronise pour te reconnecter.", "settings");
            _loginNotified = true;
            return;
        }
        _loginNotified = false;
        if (!n.Enabled || !n.NewFiles) return;
        var fresh = _s.Db.All<(string Course, long N)>("""
            SELECT COALESCE(c.folder, 'Personnel') course, COUNT(*) n FROM files f LEFT JOIN courses c ON c.id = f.course_id
            WHERE f.added_at >= @started AND f.remote_key IS NOT NULL AND f.user_deleted = 0 GROUP BY course ORDER BY n DESC
            """, new { started });
        if (fresh.Count > 0) Notifier.Show("Nouveaux documents sur EPREL", NewFilesMessage(fresh.Select(f => (f.Course, (int)f.N)).ToList()), "recent");
    }

    void CheckDeadlines(long now)
    {
        var n = Notify;
        if (!n.Enabled || !n.Deadlines) return;
        var notified = (_s.Setting("notifiedDeadlines") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => long.TryParse(x, out var v) ? v : 0).ToHashSet();
        var due = DueSoon(Queries.ListAssignments(_s.Db, now), now, notified);
        if (due.Count == 0) return;
        foreach (var a in due)
        {
            Notifier.Show($"À rendre {Fmt.TimeLeft(a.DueAt).ToLowerInvariant()}", $"{a.Name} · {a.Course}", "assignments");
            notified.Add(a.Id);
        }
        _s.SetSetting("notifiedDeadlines", string.Join(',', notified));
    }

    /// <summary>Synchronise tout de suite (bouton « Tester »), sans attendre l'intervalle.</summary>
    public Task RunNowAsync() { _lastAttempt = Fmt.Now(); return RunAsync(Fmt.Now()); }
}
