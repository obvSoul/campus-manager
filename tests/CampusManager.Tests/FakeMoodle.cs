using System.Text;
using CampusManager.Core.Sync;

namespace CampusManager.Tests;

/// <summary>Faux EPREL : l'API Moodle est simulée (format documenté). Prouve la logique de sync, pas le vrai EPREL.</summary>
public class FakeMoodle : IMoodleApi
{
    public Func<Task<List<MoodleCourse>>> Courses = () => Task.FromResult(new List<MoodleCourse> { new(7, "Programmation C S1", "R1.01") });
    public List<CourseSection> Sections = [];
    public List<Assignment> Assigns = [];
    public Dictionary<string, string> Store = [];
    public List<string> Downloads = [];
    public Exception? FailNext;
    public Func<long, Task<string?>>? Submission;
    public Func<long, Task<List<ActionEvent>>>? Events;

    public static ModuleContent File(string filename, string content, long t = 100) =>
        new("file", filename, content.Length, $"https://e/pluginfile.php/1/mod_resource/content/1/{filename}?forcedownload=1", t);

    public Task<List<MoodleCourse>> CoursesAsync() => Courses();
    public Task<List<CourseSection>> ContentsAsync(long courseId) => Task.FromResult(Sections);
    public Task<List<Assignment>> AssignmentsAsync(IReadOnlyList<long> courseIds) => Task.FromResult(Assigns);
    public Task<byte[]> DownloadAsync(string fileurl)
    {
        if (FailNext is { } e) { FailNext = null; throw e; }
        Downloads.Add(fileurl);
        return Task.FromResult(Encoding.UTF8.GetBytes(Store[fileurl.Split('?')[0].Split('/')[^1]]));
    }
    public Task<string?> SubmissionStatusAsync(long assignId) => Submission?.Invoke(assignId) ?? Task.FromResult<string?>(null);
    public Task<List<ActionEvent>> ActionEventsAsync(long fromTime) => Events?.Invoke(fromTime) ?? Task.FromResult(new List<ActionEvent>());
}
