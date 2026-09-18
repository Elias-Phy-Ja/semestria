namespace SchulnetzSync.Core.Model;

/// <summary>
/// What kind of entry the feed delivered. Decided by the UID prefix, never by SUMMARY.
/// </summary>
public enum SchulnetzEventType
{
    /// <summary>Lesson. Only parsed to look up exam rooms, never written to the calendar.</summary>
    Lektion,

    /// <summary>Exam, UID prefix "P_".</summary>
    Pruefung,

    /// <summary>School appointment, UID prefix "T_".</summary>
    Termin
}
