namespace SchulnetzSync.Core.Calendar;

/// <summary>
/// GUIDs and Graph property IDs for the four extended properties that
/// SchulnetzSync stamps on every calendar event it creates.
///
/// The format for singleValueExtendedProperty is:
///   "String {GUID} Name"
///
/// These GUIDs are fixed and must never change once events exist in production
/// calendars — changing them would make the app unable to find its own events.
/// </summary>
public static class ExtendedPropertyIds
{
    // One GUID family for all four properties keeps them easy to identify
    // in a Graph API trace.
    private const string Namespace = "BC709B49-3C5D-4FB0-AA36-C9A0EFAE";

    /// <summary>Stores the stable Schulnetz key, e.g. "P_65100".</summary>
    public const string Key         = $"String {{{Namespace}DF1E}} schulnetzKey";

    /// <summary>Stores the event type ("Pruefung" or "Termin").</summary>
    public const string Type        = $"String {{{Namespace}DF1F}} schulnetzType";

    /// <summary>Content hash used to detect changes without re-reading every field.</summary>
    public const string Hash        = $"String {{{Namespace}DF20}} schulnetzHash";

    /// <summary>ISO date set when the event is first absent from the feed.</summary>
    public const string MissingSince = $"String {{{Namespace}DF21}} schulnetzMissingSince";
}
