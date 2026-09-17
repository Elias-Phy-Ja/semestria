namespace SchulnetzSync.UI;

/// <summary>
/// The colours users can pick, shared by the calendar and the task lists so
/// both offer the same choice.
/// </summary>
public static class ColorPalette
{
    /// <summary>
    /// All colours offered in the pickers, in display order.
    ///
    /// Vier Reihen zu je sieben, damit die Farbkreise in den schmalen Panels
    /// sauber umbrechen: kräftig warm, kräftig kühl, hell, gedeckt. Wem das
    /// nicht reicht, der mischt sich eine eigene Farbe.
    /// </summary>
    public static readonly (string Hex, string Name)[] All =
    [
        // kräftig, warm → grün
        ("#DC2626", "Rot"),
        ("#EA580C", "Orange"),
        ("#D97706", "Amber"),
        ("#CA8A04", "Gelb"),
        ("#65A30D", "Limette"),
        ("#16A34A", "Grün"),
        ("#059669", "Smaragd"),

        // kräftig, kühl
        ("#0D9488", "Türkis"),
        ("#0891B2", "Cyan"),
        ("#0EA5E9", "Himmelblau"),
        ("#2563EB", "Blau"),
        ("#4F46E5", "Indigo"),
        ("#7C3AED", "Violett"),
        ("#A21CAF", "Lila"),

        // hell
        ("#DB2777", "Pink"),
        ("#E11D48", "Himbeere"),
        ("#FB7185", "Koralle"),
        ("#FB923C", "Pfirsich"),
        ("#FACC15", "Sonnengelb"),
        ("#34D399", "Mint"),
        ("#60A5FA", "Eisblau"),

        // gedeckt
        ("#A78BFA", "Flieder"),
        ("#92400E", "Braun"),
        ("#4D7C0F", "Oliv"),
        ("#1E3A8A", "Marine"),
        ("#6B7280", "Grau"),
        ("#475569", "Schiefer"),
        ("#1E293B", "Dunkel"),
    ];

    /// <summary>
    /// Order for automatically assigned colours.
    ///
    /// Abwechselnd warm und kalt, damit benachbarte Listen sich unterscheiden.
    /// Grau und Dunkel fehlen bewusst: Grau wirkt wie «keine Farbe», Dunkel
    /// geht im dunklen Theme unter. Wählen kann man beide trotzdem.
    /// </summary>
    private static readonly string[] AutoOrder =
    [
        "#2563EB", "#16A34A", "#DC2626", "#7C3AED", "#EA580C", "#0D9488",
        "#DB2777", "#CA8A04", "#0EA5E9", "#A21CAF", "#65A30D", "#D97706",
    ];

    /// <summary>
    /// Picks the least used colour, so new lists spread across the palette
    /// instead of all turning blue.
    /// </summary>
    public static string NextAutoColor(IEnumerable<string> used)
    {
        var counts = used
            .GroupBy(h => h, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        // OrderBy ist stabil: bei Gleichstand gewinnt die Reihenfolge in AutoOrder.
        return AutoOrder.OrderBy(h => counts.GetValueOrDefault(h)).First();
    }
}
