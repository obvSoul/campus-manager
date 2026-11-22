namespace CampusManager.Core.Library;

/// <summary>Ligne de la table <c>files</c>.</summary>
public record FileRow
{
    public long Id { get; init; }
    public long? ResourceId { get; init; }
    public long CourseId { get; init; }
    public string Category { get; init; } = "";
    public string? RemoteKey { get; init; }
    public string LocalPath { get; init; } = "";
    public string OriginalName { get; init; } = "";
    public long Size { get; init; }
    public string Sha256 { get; init; } = "";
    public string? DownloadedSha256 { get; init; }
    public long? RemoteModified { get; init; }
    public long? RemoteSize { get; init; }
    public bool Favorite { get; init; }
    public bool Missing { get; init; }
    public bool RemoteDeleted { get; init; }
    public bool UserDeleted { get; init; }
    public long AddedAt { get; init; }
}

public enum ImportStatus { Added, Unchanged, Updated, Conflict, Duplicate }
public record ImportResult(ImportStatus Status, long FileId, string Path);

/// <param name="RemoteKey">Identité côté EPREL ; null = fichier personnel.</param>
/// <param name="FetchBytes">Appelé seulement si le téléchargement est nécessaire.</param>
public record ImportRequest(long CourseId, long? ResourceId, string Category, string Name, string? RemoteKey,
    long? RemoteModified, long? RemoteSize, Func<Task<byte[]>> FetchBytes);

public record DropResult(int Imported, int Moved, int Duplicates, List<string> Skipped);
