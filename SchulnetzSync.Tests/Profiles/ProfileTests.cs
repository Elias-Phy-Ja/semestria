using SchulnetzSync.Core.Profiles;

namespace SchulnetzSync.Tests.Profiles;

/// <summary>
/// Profiles behave like separate installations. The risky parts are the move of existing
/// data into the first profile and the Outlook ownership rule, which keeps one person's
/// sync away from another person's exams.
/// </summary>
public class ProfileTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 24, 10, 0, 0, TimeSpan.FromHours(2));

    // ── Registry ────────────────────────────────────────────────────────

    [Fact]
    public void Initial_OwnsTheOldOutlookEvents()
    {
        var reg = ProfileRegistry.CreateInitial("Elias", Now);

        Assert.True(reg.Active.OwnsUntaggedEvents);
        Assert.Null(reg.PreviousId);
    }

    [Fact]
    public void Add_ActivatesTheNewProfile_AndRemembersTheOldOne()
    {
        var reg = ProfileRegistry.CreateInitial("Elias", Now);
        var first = reg.ActiveId;

        reg = reg.Add("Lea", Now, out var lea);

        Assert.Equal(lea.Id, reg.ActiveId);
        Assert.Equal(first, reg.PreviousId);
        Assert.False(lea.OwnsUntaggedEvents);
        Assert.NotEqual(reg.Profiles[0].ColorHex, lea.ColorHex);
    }

    [Fact]
    public void Remove_ActiveProfile_FallsBackToThePreviousOne()
    {
        var reg = ProfileRegistry.CreateInitial("Elias", Now).Add("Lea", Now, out _).Add("Noah", Now, out var noah);
        var lea = reg.Profiles[1];

        reg = reg.Remove(noah.Id);

        Assert.Equal(lea.Id, reg.ActiveId);
        Assert.Equal(2, reg.Profiles.Count);
    }

    [Fact]
    public void Remove_TheLastProfile_IsRefused()
        => Assert.Throws<InvalidOperationException>(() =>
        {
            var reg = ProfileRegistry.CreateInitial("Elias", Now);
            reg.Remove(reg.ActiveId);
        });

    [Fact]
    public void Activate_UnknownProfile_IsRefused()
        => Assert.Throws<ArgumentException>(() => ProfileRegistry.CreateInitial("Elias", Now).Activate(Guid.NewGuid()));

    [Theory]
    [InlineData("  Elias  ", "Elias")]
    [InlineData("", "Profil")]
    [InlineData(null, "Profil")]
    [InlineData("Ein sehr sehr sehr langer Profilname", "Ein sehr sehr sehr langer Prof")]
    public void Names_AreCleaned(string? input, string expected)
        => Assert.Equal(expected, ProfileRegistry.CleanName(input));

    [Theory]
    [InlineData("Elias Wyss", "EW")]
    [InlineData("elias", "E")]
    [InlineData("Lea Marie Muster", "LM")]
    public void Initials_ForTheAvatar(string name, string expected)
        => Assert.Equal(expected, new Profile(Guid.NewGuid(), name, "#000000", Now, false).Initials);

    [Fact]
    public void Registry_RoundTripsThroughProfilesJson()
    {
        var dir = Directory.CreateTempSubdirectory("semestria-test-").FullName;
        try
        {
            var reg = ProfileRegistry.CreateInitial("Elias", Now).Add("Lea", Now, out _);
            ProfileStore.Save(dir, reg);

            var back = ProfileStore.Load(dir)!;

            Assert.Equal(reg.ActiveId, back.ActiveId);
            Assert.Equal(reg.Profiles, back.Profiles);
            Assert.DoesNotContain("Initials", File.ReadAllText(Path.Combine(dir, "profiles.json")));
        }
        finally { Directory.Delete(dir, true); }
    }

    // ── Outlook ownership ───────────────────────────────────────────────

    [Fact]
    public void Owner_SeesOnlyItsOwnTaggedEvents()
    {
        var mine  = new CalendarOwner("aaaa", OwnsUntagged: false);

        Assert.True(mine.Owns("aaaa"));
        Assert.True(mine.Owns("AAAA"));
        Assert.False(mine.Owns("bbbb"));
        Assert.False(mine.Owns(null));
        Assert.False(mine.Owns(""));
    }

    [Fact]
    public void FirstProfile_AlsoOwnsEventsFromBeforeProfiles()
    {
        var first = new CalendarOwner("aaaa", OwnsUntagged: true);

        Assert.True(first.Owns(null));
        Assert.True(first.Owns(""));
        Assert.False(first.Owns("bbbb"));
    }

    // ── Migration of the old data ───────────────────────────────────────

    [Fact]
    public void Migration_MovesPersonalData_AndLeavesGlobalFiles()
    {
        var root = Directory.CreateTempSubdirectory("semestria-test-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "config.json"), "{}");
            File.WriteAllText(Path.Combine(root, "tasks.json"), "[]");
            File.WriteAllText(Path.Combine(root, "token_cache.bin"), "x");
            File.WriteAllText(Path.Combine(root, "crash.log"), "log");
            File.WriteAllText(Path.Combine(root, "update-prefs.json"), "{}");
            Directory.CreateDirectory(Path.Combine(root, "somefolder"));
            var id = Guid.NewGuid();

            int moved = ProfileMigration.MoveIntoProfile(root, id);

            var dir = DataPaths.DirectoryFor(root, id);
            Assert.Equal(4, moved);
            Assert.True(File.Exists(Path.Combine(dir, "config.json")));
            Assert.True(File.Exists(Path.Combine(dir, "token_cache.bin")));
            Assert.True(Directory.Exists(Path.Combine(dir, "somefolder")));
            Assert.True(File.Exists(Path.Combine(root, "crash.log")));
            Assert.True(File.Exists(Path.Combine(root, "update-prefs.json")));
            Assert.Empty(ProfileMigration.LegacyEntries(root));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Migration_NeverOverwritesWhatTheProfileAlreadyHas()
    {
        var root = Directory.CreateTempSubdirectory("semestria-test-").FullName;
        try
        {
            var id  = Guid.NewGuid();
            var dir = DataPaths.DirectoryFor(root, id);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "config.json"), "new");
            File.WriteAllText(Path.Combine(root, "config.json"), "old");

            ProfileMigration.MoveIntoProfile(root, id);

            Assert.Equal("new", File.ReadAllText(Path.Combine(dir, "config.json")));
            Assert.True(File.Exists(Path.Combine(root, "config.json")));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Migration_RunTwice_DoesNothingTheSecondTime()
    {
        var root = Directory.CreateTempSubdirectory("semestria-test-").FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "grades.json"), "[]");
            var id = Guid.NewGuid();

            ProfileMigration.MoveIntoProfile(root, id);

            Assert.Equal(0, ProfileMigration.MoveIntoProfile(root, id));
        }
        finally { Directory.Delete(root, true); }
    }
}
