namespace SchulnetzSync.UI;

/// <summary>
/// The colours users can pick, shared by the calendar and the task lists so
/// both offer the same choice.
/// </summary>
public static class ColorPalette
{
    /// <summary>
    /// All colours the pickers offer, in display order.
    ///
    /// Four rows of seven, so the circles wrap cleanly in the narrow panels: strong warm,
    /// strong cool, light, muted. Anyone who needs something else mixes their own.
    /// </summary>
    public static readonly (string Hex, string Name)[] All =
    [
        // strong, warm through to green
        ("#DC2626", "Rot"),
        ("#EA580C", "Orange"),
        ("#D97706", "Amber"),
        ("#CA8A04", "Gelb"),
        ("#65A30D", "Limette"),
        ("#16A34A", "Grün"),
        ("#059669", "Smaragd"),

        // strong, cool
        ("#0D9488", "Türkis"),
        ("#0891B2", "Cyan"),
        ("#0EA5E9", "Himmelblau"),
        ("#2563EB", "Blau"),
        ("#4F46E5", "Indigo"),
        ("#7C3AED", "Violett"),
        ("#A21CAF", "Lila"),

        // light
        ("#DB2777", "Pink"),
        ("#E11D48", "Himbeere"),
        ("#FB7185", "Koralle"),
        ("#FB923C", "Pfirsich"),
        ("#FACC15", "Sonnengelb"),
        ("#34D399", "Mint"),
        ("#60A5FA", "Eisblau"),

        // muted
        ("#A78BFA", "Flieder"),
        ("#92400E", "Braun"),
        ("#4D7C0F", "Oliv"),
        ("#1E3A8A", "Marine"),
        ("#6B7280", "Grau"),
        ("#475569", "Schiefer"),
        ("#1E293B", "Dunkel"),
    ];

    /// <summary>
    /// The order in which colours are handed out automatically.
    ///
    /// Warm and cool alternate so that neighbouring lists stay apart. Grey and dark are
    /// left out on purpose: grey reads as "no colour" and dark disappears in the dark
    /// theme. Both can still be picked by hand.
    /// </summary>
    private static readonly string[] AutoOrder =
    [
        "#2563EB", "#16A34A", "#DC2626", "#7C3AED", "#EA580C", "#0D9488",
        "#DB2777", "#CA8A04", "#0EA5E9", "#A21CAF", "#65A30D", "#D97706",
    ];

    /// <summary>
    /// Picks the least used colour, so new lists spread out over the palette instead of
    /// all ending up blue.
    /// </summary>
    public static string NextAutoColor(IEnumerable<string> used)
    {
        var counts = used
            .GroupBy(h => h, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        // OrderBy is stable, so on a tie the AutoOrder sequence decides.
        return AutoOrder.OrderBy(h => counts.GetValueOrDefault(h)).First();
    }
}
