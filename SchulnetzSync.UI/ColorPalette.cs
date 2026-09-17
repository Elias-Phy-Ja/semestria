namespace SchulnetzSync.UI;

/// <summary>
/// The colours users can pick, shared by the calendar and the task lists so
/// both offer the same choice.
/// </summary>
public static class ColorPalette
{
    /// <summary>All colours offered in the pickers, in display order.</summary>
    public static readonly (string Hex, string Name)[] All =
    [
        ("#DC2626", "Rot"),
        ("#EA580C", "Orange"),
        ("#D97706", "Amber"),
        ("#CA8A04", "Gelb"),
        ("#65A30D", "Limette"),
        ("#16A34A", "Grün"),
        ("#0D9488", "Türkis"),
        ("#0EA5E9", "Himmelblau"),
        ("#2563EB", "Blau"),
        ("#7C3AED", "Violett"),
        ("#A21CAF", "Lila"),
        ("#DB2777", "Pink"),
        ("#6B7280", "Grau"),
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
