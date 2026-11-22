using CampusManager.App.Services;
using CampusManager.Core.Queries;

namespace CampusManager.Tests;

public class AutoSyncTests
{
    const long Now = 1_800_000_000;

    [Fact]
    public void IsDue_DisabledNeverSyncs() => Assert.False(AutoSync.IsDue(null, 0, Now));

    [Fact]
    public void IsDue_NeverSyncedSyncsNow() => Assert.True(AutoSync.IsDue(null, 120, Now));

    [Fact]
    public void IsDue_WaitsForTheInterval()
    {
        Assert.False(AutoSync.IsDue(Now - 119 * 60, 120, Now));
        Assert.True(AutoSync.IsDue(Now - 120 * 60, 120, Now));
    }

    static AssignmentItem A(long id, long? due, string status = "todo", bool old = false) =>
        new(id, $"Devoir {id}", "R1.01", 1, null, due, null, status, due < Now && status != "done", 0, null, false, old);

    [Fact]
    public void DueSoon_OnlyOpenAssignmentsWithin24h_NotAlreadyNotified()
    {
        var list = new[]
        {
            A(1, Now + 3600),            // dans 1 h → oui
            A(2, Now + 23 * 3600),       // dans 23 h → oui, mais déjà notifié
            A(3, Now + 30 * 3600),       // dans 30 h → pas encore
            A(4, Now + 3600, "done"),    // terminé → non
            A(5, null),                  // sans date → non
            A(6, Now - 3600),            // déjà en retard → non (pas un rappel)
            A(7, Now + 3600, old: true), // année précédente → non
        };
        var r = AutoSync.DueSoon(list, Now, notified: new HashSet<long> { 2 });
        Assert.Equal([1L], r.Select(a => a.Id));
    }

    [Fact]
    public void NewFilesMessage_GroupsByCourse()
    {
        Assert.Equal("3 nouveaux fichiers : R1.01 (2), R1.02 (1)",
            AutoSync.NewFilesMessage([("R1.01 - Initiation au développement", 2), ("R1.02 - Développement web", 1)]));
        Assert.Equal("1 nouveau fichier : R1.05", AutoSync.NewFilesMessage([("R1.05 - Bases de données", 1)]));
    }

    [Fact]
    public void Intervals_HaveLabels()
    {
        Assert.Contains(AutoSync.Intervals, i => i.Minutes == 0 && i.Label == "Jamais");
        Assert.Contains(AutoSync.Intervals, i => i.Minutes == AutoSync.DefaultMinutes);
    }
}
