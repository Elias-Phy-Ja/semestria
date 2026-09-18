namespace SchulnetzSync.Core.Calendar;

/// <summary>
/// The four extended properties we stamp on every event we create. They are how the
/// app recognises its own entries later — without them a sync could not tell an exam
/// it wrote from an appointment the user typed in by hand.
///
/// Graph wants the id in exactly this shape:
///   "&lt;MapiPropertyType&gt; {&lt;namespaceGuid&gt;} Name &lt;propertyName&gt;"
/// The word "Name" is a literal keyword, not a placeholder — leave it out and Graph
/// answers with "PropertyId values may only be in one of the following formats...".
///
/// The GUIDs must never change once events exist out there, or the app loses track
/// of everything it has already written.
/// </summary>
public static class ExtendedPropertyIds
{
    // One GUID family for all four, which makes them easy to spot in a Graph trace.
    private const string Namespace = "BC709B49-3C5D-4FB0-AA36-C9A0EFAE";

    /// <summary>The Schulnetz key, e.g. "P_65100".</summary>
    public const string Key         = $"String {{{Namespace}DF1E}} Name schulnetzKey";

    /// <summary>Event type, "Pruefung" or "Termin".</summary>
    public const string Type        = $"String {{{Namespace}DF1F}} Name schulnetzType";

    /// <summary>Content hash, so a change can be spotted without comparing every field.</summary>
    public const string Hash        = $"String {{{Namespace}DF20}} Name schulnetzHash";

    /// <summary>ISO date of the first run in which the event was missing from the feed.</summary>
    public const string MissingSince = $"String {{{Namespace}DF21}} Name schulnetzMissingSince";
}
