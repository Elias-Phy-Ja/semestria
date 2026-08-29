namespace SchulnetzSync.Core.Model;

/// <summary>
/// Classifies a calendar entry from the Schulnetz iCal feed.
/// Classification is based exclusively on the UID prefix — never on SUMMARY.
/// </summary>
public enum SchulnetzEventType
{
    /// <summary>Regular lesson. Parsed for room lookup only; never written to the calendar.</summary>
    Lektion,

    /// <summary>Exam (UID prefix "P_").</summary>
    Pruefung,

    /// <summary>School appointment (UID prefix "T_").</summary>
    Termin
}
