using Dapper;
using Microsoft.Data.Sqlite;

namespace CampusManager.Core.Db;

/// <summary>
/// Base SQLite locale. Une seule connexion, protégée par un verrou : la synchronisation (thread de fond)
/// et l'interface peuvent l'utiliser en même temps sans « database is locked ».
/// </summary>
public sealed class Db : IDisposable
{
    readonly SqliteConnection _c;
    readonly object _lock = new();

    static Db() => DefaultTypeMap.MatchNamesWithUnderscores = true;

    Db(SqliteConnection c) => _c = c;

    public static Db Open(string path)
    {
        var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        c.Open();
        var db = new Db(c);
        Schema.Apply(db);
        return db;
    }

    public int Execute(string sql, object? p = null) { lock (_lock) return _c.Execute(sql, p); }
    public T? One<T>(string sql, object? p = null) { lock (_lock) return _c.QueryFirstOrDefault<T>(sql, p); }
    public List<T> All<T>(string sql, object? p = null) { lock (_lock) return _c.Query<T>(sql, p).AsList(); }
    public T Scalar<T>(string sql, object? p = null) { lock (_lock) return _c.ExecuteScalar<T>(sql, p)!; }

    /// <summary>Exécute un INSERT et retourne l'identifiant de la ligne créée.</summary>
    public long Insert(string sql, object? p = null)
    {
        lock (_lock) { _c.Execute(sql, p); return _c.ExecuteScalar<long>("SELECT last_insert_rowid()"); }
    }

    /// <summary>Plusieurs écritures d'un bloc (le verrou est réentrant).</summary>
    public void Tx(Action body)
    {
        lock (_lock)
        {
            _c.Execute("BEGIN TRANSACTION"); // un seul mot serait pris par Dapper pour une procédure stockée
            try { body(); _c.Execute("COMMIT TRANSACTION"); }
            catch { _c.Execute("ROLLBACK TRANSACTION"); throw; }
        }
    }

    public void Dispose() { lock (_lock) _c.Dispose(); }
}
