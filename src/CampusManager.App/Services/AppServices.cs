using CampusManager.Core.Db;
using CampusManager.Core.Library;
using CampusManager.Core.Queries;
using CampusManager.Core.Sync;

namespace CampusManager.App.Services;

/// <summary>
/// Services de l'appli : base, bibliothèque, synchronisation. Un seul propriétaire de la base (ce processus).
/// Les événements peuvent être levés depuis un thread de fond : l'interface les relaie sur son thread.
/// </summary>
public sealed class AppServices : IDisposable
{
    public const string Eprel = "https://eprel.u-pec.fr";

    public Db Db { get; }
    public Library Lib { get; }
    public string DataDir { get; }
    public SyncState State { get; private set; }
    public event Action<SyncState>? StateChanged;
    /// <summary>Les données ont changé (fichier déplacé, synchro terminée…) : écrans et widget se rafraîchissent.</summary>
    public event Action? DataChanged;

    readonly ITokenStore _tokens;
    readonly Func<Task<string>> _login;
    readonly Func<string, IMoodleApi> _client;
    readonly Func<string, Task<bool>> _checkToken;
    readonly object _logLock = new();

    /// <param name="login">Ouvre la fenêtre de connexion UPEC et retourne le jeton.</param>
    /// <param name="client">Crée le client Moodle pour un jeton.</param>
    /// <param name="checkToken">Vérifie un jeton (lève une MoodleException d'authentification s'il est refusé).</param>
    public AppServices(string dataDir, string? defaultRoot, ITokenStore tokens, Func<Task<string>> login,
        Func<string, IMoodleApi> client, Func<string, Task<bool>> checkToken)
    {
        DataDir = dataDir;
        _tokens = tokens; _login = login; _client = client; _checkToken = checkToken;
        Directory.CreateDirectory(Path.Combine(dataDir, "logs"));
        Db = Db.Open(Path.Combine(dataDir, "library.db"));
        var root = Db.One<string>("SELECT value FROM settings WHERE key='root'")
            ?? defaultRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Campus Manager");
        Lib = new Library(Db, root);
        Db.Execute("INSERT OR IGNORE INTO settings(key,value) VALUES('root',@root)", new { root });
        var last = Db.One<long?>("SELECT finished_at FROM sync_log WHERE finished_at IS NOT NULL ORDER BY id DESC LIMIT 1");
        State = new(false, "", last, false);
        // Au démarrage : retrouver les fichiers déplacés dans l'Explorateur pendant que l'appli était fermée.
        try { var (rel, miss) = Lib.Reconcile(); if (rel + miss > 0) Log($"reconcile : {rel} retrouvés, {miss} absents"); }
        catch (Exception e) { Log($"reconcile : {e.Message}"); }
    }

    /// <summary>Journal technique local (jamais de jeton).</summary>
    public void Log(string msg)
    {
        lock (_logLock)
            try { File.AppendAllText(Path.Combine(DataDir, "logs", "campus.log"), $"{DateTime.UtcNow:O} {msg}\n"); }
            catch { /* journal indisponible : sans gravité */ }
    }

    public string? Setting(string key) => Db.One<string>("SELECT value FROM settings WHERE key=@key", new { key });
    public void SetSetting(string key, string value) => Db.Execute("INSERT OR REPLACE INTO settings(key,value) VALUES(@key,@value)", new { key, value });

    public void NotifyChanged() => DataChanged?.Invoke();

    void Set(SyncState s) { State = s; StateChanged?.Invoke(s); }

    public async Task<SyncState> SyncAsync(bool relogin)
    {
        if (State.Running) return State;
        Set(State with { Running = true, Message = "Connexion à EPREL…", NeedsLogin = false });
        try
        {
            var token = relogin ? null : _tokens.Load();
            if (token != null)
            {
                try { await _checkToken(token); }
                catch (MoodleException e) when (e.IsAuthError) { _tokens.Clear(); token = null; }
            }
            if (token == null)
            {
                Set(State with { Message = "Connecte-toi dans la fenêtre UPEC…" });
                token = await _login();
                _tokens.Save(token);
            }
            Set(State with { Message = "Synchronisation des cours…" });
            var api = _client(token);
            var rep = await Task.Run(() => SyncEngine.SyncAllAsync(api, Db, Lib));
            Log($"sync : {rep.Added} ajoutés, {rep.Updated} maj, {rep.Unchanged} inchangés, {rep.Errors.Count} erreurs");
            foreach (var e in rep.Errors) Log($"sync erreur : {e}");
            var msg = rep.AuthExpired ? "Session EPREL expirée : reconnecte-toi."
                : $"{rep.Added} nouveau(x) fichier(s), {rep.Updated} mis à jour"
                  + (rep.Errors.Count > 0 ? $", {rep.Errors.Count} erreur(s)" : "")
                  + (rep.Conflicts.Count > 0 ? $", {rep.Conflicts.Count} conflit(s)" : "") + ".";
            Set(new(false, msg, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), rep.AuthExpired));
        }
        catch (Exception e)
        {
            Log($"sync échec : {e.Message}");
            var offline = e is MoodleException { IsTransient: true };
            Set(State with
            {
                Running = false,
                Message = offline ? "Hors ligne : tes fichiers restent disponibles, nouvelle tentative plus tard." : e.Message,
                NeedsLogin = e.Message == "Connexion annulée",
            });
        }
        DataChanged?.Invoke();
        // Après une synchro, on rend la mémoire inutilisée au système.
        GC.Collect(2, GCCollectionMode.Aggressive, true, true);
        return State;
    }

    public void Dispose() => Db.Dispose();
}
