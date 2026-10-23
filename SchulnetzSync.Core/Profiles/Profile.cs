using System.Text.Json.Serialization;

namespace SchulnetzSync.Core.Profiles;

/// <summary>
/// One person using Semestria on this Windows account. Every profile behaves like its own
/// installation: its own feed, settings, tasks, grades and Microsoft sign-in, all in its
/// own data folder.
/// </summary>
/// <param name="Id">Stable identity; also names the data folder and tags the Outlook events.</param>
/// <param name="Name">Shown in the switcher, e.g. "Elias".</param>
/// <param name="ColorHex">Colour of the round avatar.</param>
/// <param name="CreatedAt">When the profile was added.</param>
/// <param name="OwnsUntaggedEvents">
/// True for the profile the existing data was migrated into. Outlook events written before
/// profiles existed carry no profile tag, and they belong to that person.
/// </param>
public sealed record Profile(
    Guid           Id,
    string         Name,
    string         ColorHex,
    DateTimeOffset CreatedAt,
    bool           OwnsUntaggedEvents)
{
    public const int MaxNameLength = 30;

    /// <summary>Up to two letters for the avatar: "Elias Wyss" → "EW", "elias" → "E".</summary>
    [JsonIgnore]
    public string Initials
    {
        get
        {
            var parts = Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            return parts.Length switch
            {
                0 => "?",
                1 => parts[0][..1].ToUpperInvariant(),
                _ => (parts[0][..1] + parts[^1][..1]).ToUpperInvariant()
            };
        }
    }

    /// <summary>The value written into the Outlook profile tag.</summary>
    [JsonIgnore]
    public string Tag => Id.ToString("N");

    [JsonIgnore]
    public CalendarOwner CalendarOwner => new(Tag, OwnsUntaggedEvents);
}

/// <summary>
/// Which Outlook events a profile may see and change. Two profiles can write into the
/// same calendar, so without this the sync of one would treat the exams of the other as
/// vanished and cancel them.
/// </summary>
/// <param name="ProfileTag">Tag of the profile that is syncing.</param>
/// <param name="OwnsUntagged">Whether events without any tag (written before profiles) are its own.</param>
public sealed record CalendarOwner(string ProfileTag, bool OwnsUntagged)
{
    /// <summary>True when an event with this tag belongs to the profile.</summary>
    public bool Owns(string? eventTag)
        => string.IsNullOrEmpty(eventTag)
            ? OwnsUntagged
            : string.Equals(eventTag, ProfileTag, StringComparison.OrdinalIgnoreCase);
}

/// <summary>The list of profiles and which one is active. Immutable; every change returns a new one.</summary>
/// <param name="Profiles">All profiles, in the order they were added.</param>
/// <param name="ActiveId">The profile the app runs as.</param>
/// <param name="PreviousId">
/// The profile active before the last switch. Used to go back when the setup of a freshly
/// added profile is cancelled.
/// </param>
public sealed record ProfileRegistry(IReadOnlyList<Profile> Profiles, Guid ActiveId, Guid? PreviousId)
{
    /// <summary>Avatar colours, handed out in turn.</summary>
    public static readonly string[] Colors =
        ["#5C6EF7", "#16A34A", "#D97706", "#DB2777", "#0891B2", "#7C3AED", "#DC2626", "#65A30D"];

    [JsonIgnore]
    public Profile Active => Profiles.First(p => p.Id == ActiveId);

    /// <summary>The very first registry, holding the person whose data already exists.</summary>
    public static ProfileRegistry CreateInitial(string name, DateTimeOffset now)
    {
        var first = new Profile(Guid.NewGuid(), CleanName(name), Colors[0], now, OwnsUntaggedEvents: true);
        return new ProfileRegistry([first], first.Id, null);
    }

    /// <summary>Adds a profile and makes it the active one.</summary>
    public ProfileRegistry Add(string name, DateTimeOffset now, out Profile added)
    {
        added = new Profile(Guid.NewGuid(), CleanName(name), Colors[Profiles.Count % Colors.Length], now,
                            OwnsUntaggedEvents: false);
        return new ProfileRegistry([.. Profiles, added], added.Id, ActiveId);
    }

    public ProfileRegistry Activate(Guid id)
    {
        if (Profiles.All(p => p.Id != id))
            throw new ArgumentException("Unknown profile.", nameof(id));
        return id == ActiveId ? this : this with { ActiveId = id, PreviousId = ActiveId };
    }

    public ProfileRegistry Rename(Guid id, string name)
        => this with { Profiles = Profiles.Select(p => p.Id == id ? p with { Name = CleanName(name) } : p).ToList() };

    /// <summary>
    /// Removes a profile. The last one cannot go. When the active one is removed, the app
    /// falls back to the previous profile, or else the first remaining one.
    /// </summary>
    public ProfileRegistry Remove(Guid id)
    {
        if (Profiles.Count <= 1)
            throw new InvalidOperationException("The last profile cannot be removed.");

        var rest = Profiles.Where(p => p.Id != id).ToList();
        if (rest.Count == Profiles.Count)
            throw new ArgumentException("Unknown profile.", nameof(id));

        var active = ActiveId != id ? ActiveId
                   : PreviousId is Guid prev && rest.Any(p => p.Id == prev) ? prev
                   : rest[0].Id;
        var previous = PreviousId == id ? null : PreviousId;
        return new ProfileRegistry(rest, active, previous == active ? null : previous);
    }

    /// <summary>Trims, shortens and never returns an empty name.</summary>
    public static string CleanName(string? name)
    {
        var clean = (name ?? "").Trim();
        if (clean.Length > Profile.MaxNameLength) clean = clean[..Profile.MaxNameLength].Trim();
        return clean.Length == 0 ? "Profil" : clean;
    }
}
