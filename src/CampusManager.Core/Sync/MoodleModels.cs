namespace CampusManager.Core.Sync;

/// <summary>Erreur Moodle ou réseau. Le message ne contient jamais le jeton.</summary>
public class MoodleException(string message, string code) : Exception(message)
{
    public string Code { get; } = code;
    /// <summary>Jeton expiré/refusé : il faut se reconnecter.</summary>
    public bool IsAuthError => Code is "invalidtoken" or "invalidlogin" or "accessexception" or "forcepasswordchangenotice";
    /// <summary>Réseau ou serveur indisponible : réessayer plus tard.</summary>
    public bool IsTransient => Code is "network" or "http5xx";
}

public record MoodleCourse(long Id, string Fullname, string Shortname, long? Startdate = null, long? Enddate = null);
public record ModuleContent(string Type, string Filename, long Filesize, string Fileurl, long Timemodified);
public record CourseModule(long Id, string Name, string Modname, string? Description = null, string? Url = null, List<ModuleContent>? Contents = null, long? Added = null);
public record CourseSection(long Id, string Name, List<CourseModule> Modules);
public record Assignment(long Id, long Cmid, long Course, string Name, string? Intro, long Duedate, long Allowsubmissionsfromdate, long Timemodified);
public record SiteInfo(long Userid, string Fullname);
/// <summary>Échéance telle qu'affichée dans la chronologie EPREL (tient compte des prolongations).</summary>
public record ActionEvent(string Modulename, long Instance, long Timesort, string? Url = null);

/// <summary>Ce dont le moteur de sync a besoin : permet de tester sans réseau.</summary>
public interface IMoodleApi
{
    Task<List<MoodleCourse>> CoursesAsync();
    Task<List<CourseSection>> ContentsAsync(long courseId);
    Task<List<Assignment>> AssignmentsAsync(IReadOnlyList<long> courseIds);
    Task<byte[]> DownloadAsync(string fileurl);
    /// <summary>État de rendu (« submitted », « new », « draft »…) ; null si inconnu.</summary>
    Task<string?> SubmissionStatusAsync(long assignId);
    /// <summary>Échéances actives de la chronologie.</summary>
    Task<List<ActionEvent>> ActionEventsAsync(long fromTime);
}
