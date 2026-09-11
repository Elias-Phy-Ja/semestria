namespace SchulnetzSync.UI.Model;

/// <summary>
/// A single to-do: homework, a paper to hand in, something to prepare.
///
/// Rein lokal. Aufgaben werden nie in den Outlook-Kalender geschrieben —
/// ein Kalender bildet Zeitpunkte ab, eine Aufgabe einen Zustand.
/// </summary>
/// <param name="Id">Stable identity, also used as the reminder key.</param>
/// <param name="Title">What has to be done.</param>
/// <param name="ListName">
/// The list this task belongs to, e.g. "Deutsch" or "WIR". Empty means the task
/// sits in no list; those are collected under "Ohne Liste".
/// </param>
/// <param name="Notes">Free text, optional.</param>
/// <param name="DueAt">When it has to be handed in. Null = kein fester Termin.</param>
/// <param name="RemindAt">
/// When the app should remind. Null = keine Erinnerung.
/// Ist der Zeitpunkt erreicht und die App läuft, erscheint eine Meldung im
/// Infobereich; danach wird <see cref="ReminderShown"/> gesetzt.
/// </param>
/// <param name="ReminderShown">True once the reminder has been displayed.</param>
/// <param name="IsImportant">Starred by the user; sorts to the top of its list.</param>
/// <param name="IsDone">True when ticked off.</param>
/// <param name="CreatedAt">When the task was added.</param>
/// <param name="CompletedAt">When it was ticked off, null while open.</param>
public sealed record TaskItem(
    Guid            Id,
    string          Title,
    string          ListName,
    string?         Notes,
    DateTimeOffset? DueAt,
    DateTimeOffset? RemindAt,
    bool            ReminderShown,
    bool            IsImportant,
    bool            IsDone,
    DateTimeOffset  CreatedAt,
    DateTimeOffset? CompletedAt)
{
    /// <summary>Group label; tasks without a list land in their own group.</summary>
    public string ListLabel =>
        string.IsNullOrWhiteSpace(ListName) ? NoList : ListName;

    /// <summary>Label used for tasks that belong to no list.</summary>
    public const string NoList = "Ohne Liste";

    /// <summary>Open and past its due date.</summary>
    public bool IsOverdue(DateTimeOffset now)
        => !IsDone && DueAt.HasValue && DueAt.Value < now;

    /// <summary>Reminder is due and has not been shown yet.</summary>
    public bool ReminderPending(DateTimeOffset now)
        => !IsDone && !ReminderShown && RemindAt.HasValue && RemindAt.Value <= now;
}
