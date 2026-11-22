using CampusManager.Core.Db;
using Microsoft.Data.Sqlite;

namespace CampusManager.Tests;

public class DbTests
{
    // Schéma de la toute première version (avant les migrations douces).
    const string OldSchema = """
        CREATE TABLE settings(key TEXT PRIMARY KEY, value TEXT NOT NULL);
        CREATE TABLE courses(id INTEGER PRIMARY KEY, remote_id INTEGER UNIQUE, name TEXT NOT NULL, shortname TEXT,
          semester TEXT NOT NULL DEFAULT 'Autres matières', folder TEXT NOT NULL);
        CREATE TABLE resources(id INTEGER PRIMARY KEY, course_id INTEGER NOT NULL REFERENCES courses(id), remote_id INTEGER,
          name TEXT NOT NULL, modname TEXT, section TEXT, description TEXT, category TEXT NOT NULL,
          category_source TEXT NOT NULL DEFAULT 'auto', confidence REAL NOT NULL DEFAULT 0, published_at INTEGER,
          remote_deleted INTEGER NOT NULL DEFAULT 0, UNIQUE(course_id, remote_id));
        CREATE TABLE files(id INTEGER PRIMARY KEY, resource_id INTEGER REFERENCES resources(id), course_id INTEGER REFERENCES courses(id),
          category TEXT NOT NULL, remote_key TEXT UNIQUE, local_path TEXT NOT NULL, original_name TEXT NOT NULL, size INTEGER NOT NULL,
          sha256 TEXT NOT NULL, downloaded_sha256 TEXT, remote_modified INTEGER, remote_size INTEGER, favorite INTEGER NOT NULL DEFAULT 0,
          missing INTEGER NOT NULL DEFAULT 0, remote_deleted INTEGER NOT NULL DEFAULT 0, added_at INTEGER NOT NULL);
        CREATE TABLE assignments(id INTEGER PRIMARY KEY, remote_id INTEGER UNIQUE, course_id INTEGER REFERENCES courses(id),
          name TEXT NOT NULL, description TEXT, published_at INTEGER, due_at INTEGER, status TEXT NOT NULL DEFAULT 'todo',
          priority INTEGER NOT NULL DEFAULT 0, notes TEXT, remote_deleted INTEGER NOT NULL DEFAULT 0, remote_modified INTEGER);
        CREATE TABLE sync_log(id INTEGER PRIMARY KEY, started_at INTEGER NOT NULL, finished_at INTEGER, ok INTEGER NOT NULL DEFAULT 0, summary TEXT);
        INSERT INTO courses(id,name,folder) VALUES(1,'Prog','Prog');
        INSERT INTO files(course_id,category,local_path,original_name,size,sha256,favorite,added_at)
          VALUES(1,'TD','S1\Prog\TD\td1.pdf','td1.pdf',42,'abc',1,100);
        """;

    [Fact]
    public void OpensDatabaseCreatedByElectronSchema()
    {
        using var t = new TempDir();
        var path = t.Combine("library.db");
        using (var c = new SqliteConnection($"Data Source={path}"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = OldSchema;
            cmd.ExecuteNonQuery();
        }
        using var db = Db.Open(path);
        var cols = db.All<string>("SELECT name FROM pragma_table_info('courses')")
            .Concat(db.All<string>("SELECT name FROM pragma_table_info('files')"))
            .Concat(db.All<string>("SELECT name FROM pragma_table_info('assignments')")).ToList();
        foreach (var c in new[] { "layout_manual", "startdate", "user_deleted", "cmid", "base_due_at", "submitted", "status_source" })
            Assert.Contains(c, cols);
        Assert.Equal(1L, db.Scalar<long>("SELECT COUNT(*) FROM files WHERE size=42 AND favorite=1 AND user_deleted=0"));
        Assert.Equal(0L, db.Scalar<long>("SELECT COUNT(*) FROM remote_aliases"));
    }

    [Fact]
    public async Task ConcurrentWritesFromTwoThreadsSucceed()
    {
        using var t = new TempDir();
        using var db = Db.Open(t.Combine("library.db"));
        Task Writer(string prefix) => Task.Run(() =>
        {
            for (var i = 0; i < 200; i++)
                db.Execute("INSERT INTO settings(key,value) VALUES(@k,'v')", new { k = $"{prefix}{i}" });
        });
        await Task.WhenAll(Writer("a"), Writer("b"));
        Assert.Equal(400L, db.Scalar<long>("SELECT COUNT(*) FROM settings"));
    }

    [Fact]
    public void InsertReturnsRowId()
    {
        using var db = Db.Open(":memory:");
        var id = db.Insert("INSERT INTO courses(name,folder) VALUES('A','A')");
        Assert.Equal(id, db.Scalar<long>("SELECT id FROM courses"));
    }
}
