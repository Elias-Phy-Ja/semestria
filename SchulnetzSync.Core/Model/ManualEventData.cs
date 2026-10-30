namespace SchulnetzSync.Core.Model;

/// <summary>An entry the user made by hand.</summary>
/// <param name="Id">Stable identity, the Outlook key is derived from it.</param>
/// <param name="Title">What the user called it.</param>
/// <param name="Start">Start time.</param>
/// <param name="End">End time.</param>
/// <param name="IsAllDay">True when the entry has no time of day.</param>
/// <param name="Location">Room or place, optional.</param>
/// <param name="TypeKey">"Pruefung" or "Termin".</param>
public sealed record ManualEventData(
    Guid Id,
    string Title,
    DateTimeOffset Start,
    DateTimeOffset End,
    bool IsAllDay,
    string? Location,
    string TypeKey);
