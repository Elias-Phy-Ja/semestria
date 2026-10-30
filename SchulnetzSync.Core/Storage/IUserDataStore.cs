using SchulnetzSync.Core.Comments;
using SchulnetzSync.Core.Grades;
using SchulnetzSync.Core.Model;
using SchulnetzSync.Core.Tasks;

namespace SchulnetzSync.Core.Storage;

/// <summary>
/// The user's own data of one profile. Every method works on a whole data set.
///
/// A load returns an empty list when there is nothing yet and throws when the stored data
/// cannot be read, so the caller decides what a damaged file means. A save either replaces
/// the data set completely or throws.
/// </summary>
public interface IUserDataStore
{
    /// <summary>The events of the last parsed feed, lessons included.</summary>
    IReadOnlyList<SchulnetzEvent> LoadCachedEvents();
    void SaveCachedEvents(IReadOnlyList<SchulnetzEvent> events);

    /// <summary>Entries the user made by hand.</summary>
    IReadOnlyList<ManualEventData> LoadManualEvents();
    void SaveManualEvents(IReadOnlyList<ManualEventData> events);

    /// <summary>Every task, open and done.</summary>
    IReadOnlyList<TaskItem> LoadTasks();
    void SaveTasks(IReadOnlyList<TaskItem> tasks);

    /// <summary>The names of the lists created on purpose.</summary>
    IReadOnlyList<string> LoadTaskLists();
    void SaveTaskLists(IReadOnlyList<string> names);

    /// <summary>The notes on subjects.</summary>
    IReadOnlyList<SubjectComment> LoadSubjectComments();
    void SaveSubjectComments(IReadOnlyList<SubjectComment> comments);

    /// <summary>Every grade of every period.</summary>
    IReadOnlyList<GradeEntry> LoadGrades();
    void SaveGrades(IReadOnlyList<GradeEntry> grades);
}
