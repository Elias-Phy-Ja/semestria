using System.Globalization;

namespace SchulnetzSync.Core.Tasks;

/// <summary>
/// Reads a time of day the way people actually type it.
///
/// In Switzerland times get written with a dot ("8.30") or with nothing at all
/// ("0830"). A strict "HH:mm" parse threw those away without a word and quietly
/// pushed the task to 23:59, which is how this class came to exist.
/// </summary>
public static class ClockTime
{
    /// <summary>Parses a time of day.</summary>
    /// <remarks>
    /// Takes <c>8:30</c>, <c>08:30</c>, <c>8.30</c>, <c>8h30</c>, <c>830</c>,
    /// <c>0830</c>, <c>8</c> (full hour) and <c>8h</c>.
    /// Refuses anything outside 00:00–23:59, and single-digit minutes such as
    /// <c>8:5</c> — there is no way to tell 8:05 from 8:50.
    /// </remarks>
    /// <returns>False for empty or unreadable input.</returns>
    public static bool TryParse(string? text, out TimeSpan time)
    {
        time = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        // Fold every separator people use onto the colon, then there is one shape left.
        var s = text.Trim()
                    .Replace('.', ':')
                    .Replace('h', ':')
                    .Replace('H', ':');

        int hours, minutes;

        if (s.Contains(':'))
        {
            var parts = s.Split(':');
            if (parts.Length != 2) return false;
            if (!ParseDigits(parts[0], out hours)) return false;

            if (parts[1].Length == 0)
                minutes = 0;                        // "8h" or "8:"
            else if (parts[1].Length != 2 || !ParseDigits(parts[1], out minutes))
                return false;
        }
        else
        {
            if (!s.All(char.IsAsciiDigit)) return false;

            // Digits only: the length tells us where the hour ends.
            switch (s.Length)
            {
                case 1 or 2:                        // "8", "14"
                    hours   = int.Parse(s, CultureInfo.InvariantCulture);
                    minutes = 0;
                    break;
                case 3:                             // "830"
                    hours   = int.Parse(s[..1], CultureInfo.InvariantCulture);
                    minutes = int.Parse(s[1..], CultureInfo.InvariantCulture);
                    break;
                case 4:                             // "0830"
                    hours   = int.Parse(s[..2], CultureInfo.InvariantCulture);
                    minutes = int.Parse(s[2..], CultureInfo.InvariantCulture);
                    break;
                default:
                    return false;
            }
        }

        if (hours is < 0 or > 23 || minutes is < 0 or > 59) return false;

        time = new TimeSpan(hours, minutes, 0);
        return true;
    }

    private static bool ParseDigits(string s, out int value)
    {
        value = 0;
        return s.Length is > 0 and <= 2
            && int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }
}
