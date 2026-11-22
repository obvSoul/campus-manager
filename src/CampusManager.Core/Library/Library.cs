using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using CampusManager.Core.Db;

namespace CampusManager.Core.Library;

/// <summary>
/// Bibliothèque physique : les fichiers existent réellement dans
///   &lt;racine&gt;/&lt;Semestre&gt;/&lt;Matière&gt;/&lt;Catégorie&gt;/&lt;fichier&gt;
/// et la base ne fait que les indexer. Toute opération agit d'abord sur le disque, puis sur la base.
///  - écriture atomique (fichier .part, vérifié, puis renommé) ;
///  - un fichier modifié par l'utilisateur n'est JAMAIS écrasé (la version EPREL est rangée à côté) ;
///  - rien n'est retéléchargé si la taille et la date côté EPREL n'ont pas changé ;
///  - un fichier déplacé hors de l'appli est retrouvé par son empreinte (Reconcile).
/// </summary>
public class Library
{
    readonly Db.Db _db;
    public string Root { get; }

    public Library(Db.Db db, string root)
    {
        _db = db;
        Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        Directory.CreateDirectory(Root);
    }

    static long Now() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    public string Abs(string rel) => Path.Combine(Root, rel);
    string Rel(string abs) => Path.GetRelativePath(Root, abs);

    public static string Sha256Hex(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));
    static string Sha256File(string path) { using var s = File.OpenRead(path); return Convert.ToHexStringLower(SHA256.HashData(s)); }

    (string Semester, string Folder) Course(long courseId) =>
        _db.One<(string, string)?>("SELECT semester, folder FROM courses WHERE id=@courseId", new { courseId })
        ?? throw new InvalidOperationException($"Matière inconnue ({courseId})");

    /// <summary>Crée (si besoin) et retourne le dossier réel d'une matière/catégorie.</summary>
    public string CourseDir(long courseId, string category, string? subfolder = null)
    {
        var (sem, folder) = Course(courseId);
        var parts = new List<string> { Root, Names.Sanitize(sem), Names.Sanitize(folder), Names.Sanitize(category) };
        if (subfolder != null) parts.AddRange(subfolder.Split('/', '\\', StringSplitOptions.RemoveEmptyEntries).Select(p => Names.Sanitize(p)));
        var dir = Path.Combine([.. parts]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public FileRow GetFile(long id) =>
        _db.One<FileRow>("SELECT * FROM files WHERE id=@id", new { id }) ?? throw new InvalidOperationException($"Fichier inconnu ({id})");

    static void WriteAtomic(string dest, byte[] bytes)
    {
        var tmp = dest + ".part";
        File.WriteAllBytes(tmp, bytes);
        if (new FileInfo(tmp).Length != bytes.Length) { File.Delete(tmp); throw new IOException("Écriture incomplète"); }
        File.Move(tmp, dest, overwrite: true);
    }

    public bool IsLocallyModified(FileRow f)
    {
        var p = Abs(f.LocalPath);
        return f.DownloadedSha256 != null && File.Exists(p) && Sha256File(p) != f.DownloadedSha256;
    }

    const string InsertFile = """
        INSERT INTO files(resource_id,course_id,category,remote_key,local_path,original_name,size,sha256,downloaded_sha256,remote_modified,remote_size,added_at)
        VALUES(@ResourceId,@CourseId,@Category,@RemoteKey,@LocalPath,@OriginalName,@Size,@Sha256,@DownloadedSha256,@RemoteModified,@RemoteSize,@AddedAt)
        """;

    public async Task<ImportResult> ImportFileAsync(ImportRequest req)
    {
        var now = Now();
        if (req.RemoteKey != null)
        {
            var alias = _db.One<(long FileId, long? Mod, long? Size)?>(
                "SELECT file_id, remote_modified, remote_size FROM remote_aliases WHERE remote_key=@k", new { k = req.RemoteKey });
            if (alias is { } al && al.Mod == req.RemoteModified && al.Size == req.RemoteSize)
                return new(ImportStatus.Duplicate, al.FileId, GetFile(al.FileId).LocalPath);
        }
        var existing = req.RemoteKey != null ? _db.One<FileRow>("SELECT * FROM files WHERE remote_key=@k", new { k = req.RemoteKey }) : null;

        // Supprimé volontairement par l'utilisateur : on respecte son choix, pas de nouveau téléchargement.
        if (existing is { UserDeleted: true }) return new(ImportStatus.Unchanged, existing.Id, existing.LocalPath);
        if (existing is { Missing: false } && existing.RemoteModified == req.RemoteModified && existing.RemoteSize == req.RemoteSize)
            return new(ImportStatus.Unchanged, existing.Id, existing.LocalPath);

        var bytes = await req.FetchBytes();
        if (req.RemoteSize != null && bytes.Length != req.RemoteSize)
            throw new IOException($"Téléchargement incomplet : {bytes.Length}/{req.RemoteSize} octets");
        var hash = Sha256Hex(bytes);

        if (existing != null)
        {
            var dest = Abs(existing.LocalPath);
            if (hash == existing.DownloadedSha256 && !existing.Missing)
            {
                _db.Execute("UPDATE files SET remote_modified=@m, remote_size=@s WHERE id=@id", new { m = req.RemoteModified, s = req.RemoteSize, id = existing.Id });
                return new(ImportStatus.Unchanged, existing.Id, existing.LocalPath);
            }
            if (!existing.Missing && IsLocallyModified(existing))
            {
                // Conflit : on garde la version de l'utilisateur et on range l'autre à côté.
                var d = DateTimeOffset.FromUnixTimeSeconds(req.RemoteModified ?? now).UtcDateTime.ToString("yyyy-MM-dd");
                var ext = Names.Extension(existing.OriginalName);
                var stem = existing.OriginalName[..^ext.Length];
                var side = Names.UniquePath(Path.GetDirectoryName(dest)!, $"{stem} (version EPREL {d}){ext}");
                Directory.CreateDirectory(Path.GetDirectoryName(side)!);
                WriteAtomic(side, bytes);
                var id = _db.Insert(InsertFile, new FileRow
                {
                    ResourceId = req.ResourceId, CourseId = existing.CourseId, Category = existing.Category,
                    RemoteKey = $"{req.RemoteKey}#{req.RemoteModified}", LocalPath = Rel(side), OriginalName = Path.GetFileName(side),
                    Size = bytes.Length, Sha256 = hash, DownloadedSha256 = hash, RemoteModified = req.RemoteModified, RemoteSize = req.RemoteSize, AddedAt = now,
                });
                return new(ImportStatus.Conflict, id, Rel(side));
            }
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            WriteAtomic(dest, bytes);
            _db.Execute("UPDATE files SET size=@size, sha256=@h, downloaded_sha256=@h, remote_modified=@m, remote_size=@s, missing=0, remote_deleted=0 WHERE id=@id",
                new { size = bytes.Length, h = hash, m = req.RemoteModified, s = req.RemoteSize, id = existing.Id });
            return new(ImportStatus.Updated, existing.Id, existing.LocalPath);
        }

        // Même contenu déjà présent dans cette matière/catégorie : pas de second exemplaire.
        var dup = _db.One<FileRow>("SELECT * FROM files WHERE course_id=@c AND category=@cat AND sha256=@h AND missing=0 LIMIT 1",
            new { c = req.CourseId, cat = req.Category, h = hash });
        if (dup != null)
        {
            if (req.RemoteKey != null)
                _db.Execute("INSERT OR REPLACE INTO remote_aliases(remote_key,file_id,remote_modified,remote_size) VALUES(@k,@id,@m,@s)",
                    new { k = req.RemoteKey, id = dup.Id, m = req.RemoteModified, s = req.RemoteSize });
            return new(ImportStatus.Duplicate, dup.Id, dup.LocalPath);
        }

        var target = Names.UniquePath(CourseDir(req.CourseId, req.Category), Names.Sanitize(req.Name));
        WriteAtomic(target, bytes);
        var newId = _db.Insert(InsertFile, new FileRow
        {
            ResourceId = req.ResourceId, CourseId = req.CourseId, Category = req.Category, RemoteKey = req.RemoteKey,
            LocalPath = Rel(target), OriginalName = req.Name, Size = bytes.Length, Sha256 = hash,
            DownloadedSha256 = req.RemoteKey != null ? hash : null, RemoteModified = req.RemoteModified, RemoteSize = req.RemoteSize, AddedAt = now,
        });
        return new(ImportStatus.Added, newId, Rel(target));
    }

    /// <summary>Déplace un fichier (autre catégorie, matière ou sous-dossier) : disque d'abord, base ensuite.</summary>
    public FileRow MoveFile(long id, long? courseId = null, string? category = null, string? subfolder = null)
    {
        var f = GetFile(id);
        var c = courseId ?? f.CourseId;
        var cat = category ?? f.Category;
        var dest = Names.UniquePath(CourseDir(c, cat, subfolder), Path.GetFileName(f.LocalPath));
        MoveOnDisk(Abs(f.LocalPath), dest);
        _db.Execute("UPDATE files SET course_id=@c, category=@cat, local_path=@p WHERE id=@id", new { c, cat, p = Rel(dest), id });
        // Correction manuelle de classement : retenue pour les prochaines synchronisations.
        if (category != null)
            _db.Execute("UPDATE resources SET category=@cat, category_source='manual' WHERE id=(SELECT resource_id FROM files WHERE id=@id)", new { cat, id });
        return GetFile(id);
    }

    public FileRow RenameFile(long id, string newName)
    {
        var f = GetFile(id);
        var dest = Names.UniquePath(Path.GetDirectoryName(Abs(f.LocalPath))!, Names.Sanitize(newName));
        MoveOnDisk(Abs(f.LocalPath), dest);
        _db.Execute("UPDATE files SET local_path=@p WHERE id=@id", new { p = Rel(dest), id });
        return GetFile(id);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    static extern uint GetLongPathNameW(string shortPath, StringBuilder longPath, uint size);

    /// <summary>Chemin complet en noms longs (« PRENOM~1 » → « Prénom Nom »), même si la fin n'existe plus.</summary>
    static string LongPath(string path)
    {
        var full = Path.GetFullPath(path);
        var tail = new Stack<string>();
        var head = full;
        while (!string.IsNullOrEmpty(head) && !Path.Exists(head))
        {
            tail.Push(Path.GetFileName(head));
            head = Path.GetDirectoryName(head);
        }
        if (string.IsNullOrEmpty(head)) return full;
        var sb = new StringBuilder(1024);
        var n = GetLongPathNameW(head, sb, (uint)sb.Capacity);
        var resolved = n > 0 && n < sb.Capacity ? sb.ToString() : head;
        return Path.Combine([resolved, .. tail]);
    }

    /// <summary>Fichier de la bibliothèque à ce chemin absolu (insensible à la casse, noms courts acceptés).</summary>
    public FileRow? FindByAbsPath(string abs)
    {
        var rel = Path.GetRelativePath(LongPath(Root), LongPath(abs));
        if (rel.StartsWith("..") || Path.IsPathRooted(rel)) return null;
        // lower() de SQLite ignore les lettres accentuées (« È ») : comparaison faite en C#, comme Windows.
        return _db.One<FileRow>("SELECT * FROM files WHERE local_path=@rel AND user_deleted=0", new { rel })
            ?? _db.All<FileRow>("SELECT * FROM files WHERE user_deleted=0 AND length(local_path)=@n", new { n = rel.Length })
                  .FirstOrDefault(f => string.Equals(f.LocalPath, rel, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Copie un fichier personnel (glissé depuis Windows) dans la bibliothèque. L'original n'est pas touché.</summary>
    public ImportResult ImportLocal(string src, long courseId, string category)
    {
        var bytes = File.ReadAllBytes(src);
        var hash = Sha256Hex(bytes);
        var dup = _db.One<FileRow>("SELECT * FROM files WHERE course_id=@courseId AND category=@category AND sha256=@hash AND missing=0 AND user_deleted=0 LIMIT 1",
            new { courseId, category, hash });
        if (dup != null) return new(ImportStatus.Duplicate, dup.Id, dup.LocalPath);
        var dest = Names.UniquePath(CourseDir(courseId, category), Names.Sanitize(Path.GetFileName(src)));
        WriteAtomic(dest, bytes);
        var id = _db.Insert("INSERT INTO files(course_id,category,local_path,original_name,size,sha256,added_at) VALUES(@courseId,@category,@p,@n,@s,@hash,@now)",
            new { courseId, category, p = Rel(dest), n = Path.GetFileName(src), s = bytes.Length, hash, now = Now() });
        return new(ImportStatus.Added, id, Rel(dest));
    }

    /// <summary>
    /// Dépôt de fichiers sur une matière (et éventuellement une catégorie) : un fichier de la bibliothèque est
    /// déplacé (garde sa catégorie si aucune n'est visée) ; un fichier extérieur est copié (catégorie visée,
    /// sinon « Personnel »). Les dossiers sont ignorés et signalés.
    /// </summary>
    public DropResult Drop(IEnumerable<string> paths, long courseId, string? category)
    {
        int imported = 0, moved = 0, duplicates = 0;
        var skipped = new List<string>();
        Course(courseId); // vérifie que la matière existe
        foreach (var p in paths)
        {
            var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(p));
            try
            {
                if (Directory.Exists(p)) { skipped.Add($"{name} (dossier)"); continue; }
                if (!File.Exists(p)) { skipped.Add($"{name} (introuvable)"); continue; }
                var known = FindByAbsPath(p);
                if (known != null)
                {
                    var cat = category ?? known.Category;
                    if (known.CourseId == courseId && known.Category == cat) continue; // déjà au bon endroit
                    MoveFile(known.Id, courseId, cat != known.Category ? cat : null);
                    moved++;
                }
                else if (ImportLocal(p, courseId, category ?? "Personnel").Status == ImportStatus.Duplicate) duplicates++;
                else imported++;
            }
            catch (Exception e) { skipped.Add($"{name} ({e.Message})"); }
        }
        return new(imported, moved, duplicates, skipped);
    }

    /// <summary>Après mise à la corbeille Windows (faite par l'appelant) : l'entrée est conservée mais masquée.</summary>
    public void MarkDeleted(long id) => _db.Execute("UPDATE files SET user_deleted=1 WHERE id=@id", new { id });
    public void SetFavorite(long id, bool on) => _db.Execute("UPDATE files SET favorite=@on WHERE id=@id", new { on = on ? 1 : 0, id });
    public string CreateFolder(long courseId, string category, string subfolder) => Rel(CourseDir(courseId, category, subfolder));

    /// <summary>Change le semestre et/ou le dossier d'une matière : tous ses fichiers sont déplacés sur le disque.</summary>
    public void RelocateCourse(long courseId, string semester, string folder)
    {
        var old = Course(courseId);
        var oldDir = Path.Combine(Root, Names.Sanitize(old.Semester), Names.Sanitize(old.Folder));
        var files = _db.All<(long Id, string LocalPath)>("SELECT id, local_path FROM files WHERE course_id=@courseId", new { courseId });
        _db.Execute("UPDATE courses SET semester=@semester, folder=@folder WHERE id=@courseId", new { semester, folder, courseId });
        var newDir = Path.Combine(Root, Names.Sanitize(semester), Names.Sanitize(folder));
        foreach (var (id, localPath) in files)
        {
            var from = Abs(localPath);
            if (!File.Exists(from) || !from.StartsWith(oldDir, StringComparison.OrdinalIgnoreCase)) continue; // rangé ailleurs à la main
            var to = Names.UniquePath(Path.Combine(newDir, Path.GetDirectoryName(Path.GetRelativePath(oldDir, from))!), Path.GetFileName(from));
            MoveOnDisk(from, to);
            _db.Execute("UPDATE files SET local_path=@p WHERE id=@id", new { p = Rel(to), id });
        }
        RemoveEmptyDirs(oldDir);
        RemoveEmptyDirs(Path.GetDirectoryName(oldDir)!);
    }

    void RemoveEmptyDirs(string dir)
    {
        if (!Directory.Exists(dir) || string.Equals(Path.TrimEndingDirectorySeparator(dir), Root, StringComparison.OrdinalIgnoreCase)) return;
        foreach (var d in Directory.GetDirectories(dir)) RemoveEmptyDirs(d);
        if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir); // ne supprime qu'un dossier VIDE
    }

    static void MoveOnDisk(string from, string to)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        File.Move(from, to); // .NET copie puis supprime si les volumes diffèrent
    }

    /// <summary>
    /// Retrouve les fichiers déplacés/renommés depuis l'Explorateur (taille + empreinte) et marque ceux
    /// qui ont vraiment disparu. Ne supprime rien.
    /// </summary>
    public (int Relinked, int Missing) Reconcile()
    {
        var rows = _db.All<FileRow>("SELECT * FROM files");
        var lost = rows.Where(f => !File.Exists(Abs(f.LocalPath))).ToList();
        int relinked = 0, missing = 0;
        if (lost.Count > 0)
        {
            var bySize = new Dictionary<long, List<string>>();
            foreach (var p in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
            {
                if (p.EndsWith(".part", StringComparison.OrdinalIgnoreCase)) continue;
                var s = new FileInfo(p).Length;
                if (!bySize.TryGetValue(s, out var l)) bySize[s] = l = [];
                l.Add(p);
            }
            var taken = rows.Where(f => File.Exists(Abs(f.LocalPath))).Select(f => f.LocalPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var f in lost)
            {
                var hit = bySize.GetValueOrDefault(f.Size)?.FirstOrDefault(p => !taken.Contains(Rel(p)) && Sha256File(p) == f.Sha256);
                if (hit != null)
                {
                    taken.Add(Rel(hit));
                    _db.Execute("UPDATE files SET local_path=@p, missing=0 WHERE id=@id", new { p = Rel(hit), id = f.Id });
                    relinked++;
                }
                else
                {
                    _db.Execute("UPDATE files SET missing=1 WHERE id=@id", new { id = f.Id });
                    missing++;
                }
            }
        }
        foreach (var f in rows)
            if (f.Missing && File.Exists(Abs(f.LocalPath))) _db.Execute("UPDATE files SET missing=0 WHERE id=@id", new { id = f.Id });
        return (relinked, missing);
    }
}
