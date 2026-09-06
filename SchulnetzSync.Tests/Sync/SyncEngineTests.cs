using SchulnetzSync.Core.Feed;
using SchulnetzSync.Core.Model;
using SchulnetzSync.Core.Sync;

namespace SchulnetzSync.Tests.Sync;

/// <summary>
/// Tests for <see cref="SyncEngine.Build"/>.
/// All tests use synthetic data — no network, no files, no clock.
/// </summary>
public class SyncEngineTests
{
    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------
    private static readonly DateTimeOffset T0 =
        new(2026, 9, 1, 10, 0, 0, TimeSpan.FromHours(2));

    private static SchulnetzEvent MakePruefung(
        string key    = "P_1",
        string summary = "Mathe",
        string? location = null,
        DateTimeOffset? start = null,
        DateTimeOffset? end   = null)
    {
        var s = start ?? T0;
        // Ende relativ zum Start, sonst liegt es bei abweichendem start davor
        var e = end   ?? s.AddMinutes(45);
        return new SchulnetzEvent(key, $"uid-{key}", SchulnetzEventType.Pruefung,
            s, e, false, summary, location);
    }

    private static SchulnetzEvent MakeTermin(
        string key = "T_1",
        DateTimeOffset? start = null,
        DateTimeOffset? end   = null)
    {
        var s = start ?? T0;
        return new SchulnetzEvent(key, $"uid-{key}", SchulnetzEventType.Termin,
            s, end ?? s.AddHours(1), false, "Termin", null);
    }

    private static SchulnetzEvent MakeLektion(
        string key   = "20260901et48167et10:00et10:45et039",
        string room  = "039",
        DateTimeOffset? start = null)
    {
        var s = start ?? T0;
        return new SchulnetzEvent(key, $"uid-{key}", SchulnetzEventType.Lektion,
            s, s.AddMinutes(45), false, "DEU_I26A", room);
    }

    private static SchulnetzEvent MakeManual(
        string title = "Lerngruppe",
        SchulnetzEventType type = SchulnetzEventType.Termin,
        DateTimeOffset? start = null)
    {
        var s   = start ?? T0;
        var key = EventKeys.ForManual(Guid.NewGuid());
        return new SchulnetzEvent(key, key, type, s, s.AddHours(1), false, title, null);
    }

    private static TrackedEvent Track(SchulnetzEvent ev, DateTimeOffset? missingSince = null)
        => new(
            CalendarEventId: $"cal-{ev.Key}",
            Key:             ev.Key,
            Type:            ev.Type,
            Hash:            SyncEngine.ComputeHash(ev),
            Start:           ev.Start,
            MissingSince:    missingSince);

    private static SyncOptions DefaultOptions => new();

    private static SyncPlan Run(
        IReadOnlyList<SchulnetzEvent> feed,
        IReadOnlyList<TrackedEvent>   tracked,
        SyncOptions?                  options = null,
        DateTimeOffset?               now     = null)
        => SyncEngine.Build(feed, tracked, options ?? DefaultOptions,
            FeedHealth.Healthy, now ?? T0);

    // -----------------------------------------------------------------------
    // Create
    // -----------------------------------------------------------------------

    [Fact]
    public void NewFeedEvent_ProducesCreate()
    {
        var plan = Run([MakePruefung()], []);
        Assert.Single(plan.Actions, a => a.Kind == SyncActionKind.Create);
    }

    // -----------------------------------------------------------------------
    // Update
    // -----------------------------------------------------------------------

    [Fact]
    public void ChangedContent_ProducesUpdate_NotDeletePlusCreate()
    {
        var original  = MakePruefung(summary: "Mathe alt");
        var shifted   = original with { Summary = "Mathe neu" };
        var tracked   = Track(original);

        var plan = Run([shifted], [tracked]);

        Assert.Single(plan.Actions, a => a.Kind == SyncActionKind.Update);
        Assert.DoesNotContain(plan.Actions, a => a.Kind == SyncActionKind.Delete);
        Assert.DoesNotContain(plan.Actions, a => a.Kind == SyncActionKind.Create);
    }

    [Fact]
    public void RescheduledExam_ProducesExactlyOneUpdate()
    {
        // Shift start time → hash changes → Update (not Delete+Create)
        var original = MakePruefung(start: T0);
        var shifted  = original with { Start = T0.AddDays(1), End = T0.AddDays(1).AddMinutes(45) };
        var tracked  = Track(original);

        var plan = Run([shifted], [tracked]);

        Assert.Equal(1, plan.Actions.Count);
        Assert.Equal(SyncActionKind.Update, plan.Actions[0].Kind);
    }

    // -----------------------------------------------------------------------
    // No-op (idempotence)
    // -----------------------------------------------------------------------

    [Fact]
    public void UnchangedFeed_SecondRun_ProducesZeroActions()
    {
        var ev      = MakePruefung();
        var tracked = Track(ev);

        var plan = Run([ev], [tracked]);

        Assert.Empty(plan.Actions);
    }

    // -----------------------------------------------------------------------
    // FlagMissing / ClearMissing / Delete
    // -----------------------------------------------------------------------

    [Fact]
    public void MissingExam_FirstRun_ProducesFlagMissing()
    {
        var tracked = Track(MakePruefung(), missingSince: null);

        // Feed is empty for this exam's key — but start is in the future.
        var plan = Run([], [tracked], now: T0.AddMinutes(-60));

        Assert.Single(plan.Actions, a => a.Kind == SyncActionKind.FlagMissing);
    }

    [Fact]
    public void MissingExam_After24h_ProducesMarkCancelled()
    {
        var ev      = MakePruefung();
        var tracked = Track(ev, missingSince: T0.AddHours(-25));

        // Feed is empty for this key; event is still in the future.
        var plan = Run([], [tracked], now: T0.AddMinutes(-60));

        Assert.Single(plan.Actions, a => a.Kind == SyncActionKind.MarkCancelled);
    }

    [Fact]
    public void MissingTermin_After24h_ProducesDelete()
    {
        // CancelInsteadOfDelete only applies to Pruefung.
        var ev      = MakeTermin();
        var tracked = Track(ev, missingSince: T0.AddHours(-25));

        var plan = Run([], [tracked], now: T0.AddMinutes(-60));

        Assert.Single(plan.Actions, a => a.Kind == SyncActionKind.Delete);
    }

    [Fact]
    public void ReappearedExam_ProducesClearMissing()
    {
        var ev      = MakePruefung();
        var tracked = Track(ev, missingSince: T0.AddHours(-1));

        var plan = Run([ev], [tracked]);

        Assert.Single(plan.Actions, a => a.Kind == SyncActionKind.ClearMissing);
    }

    // -----------------------------------------------------------------------
    // Disabled types
    // -----------------------------------------------------------------------

    [Fact]
    public void DisabledType_ProducesZeroActions_EvenWithTrackedEntries()
    {
        var opts    = new SyncOptions { EnabledTypes = new HashSet<SchulnetzEventType>
            { SchulnetzEventType.Pruefung } }; // Termin disabled

        var termin  = MakeTermin();
        var tracked = Track(termin, missingSince: T0.AddHours(-48));

        var plan = SyncEngine.Build([termin], [tracked], opts, FeedHealth.Healthy, T0);

        Assert.Empty(plan.Actions);
    }

    // -----------------------------------------------------------------------
    // Past events are never deleted
    // -----------------------------------------------------------------------

    [Fact]
    public void PastEvent_MissingFromFeed_ProducesNoAction()
    {
        var pastTime = T0.AddDays(-1);
        var ev       = MakePruefung(start: pastTime, end: pastTime.AddMinutes(45));
        var tracked  = Track(ev, missingSince: T0.AddHours(-48));

        // now > ev.Start → past event
        var plan = Run([], [tracked], now: T0);

        Assert.Empty(plan.Actions);
    }

    // -----------------------------------------------------------------------
    // Lektionen are always ignored
    // -----------------------------------------------------------------------

    [Fact]
    public void Lektion_IsNeverCreatedOrTracked()
    {
        var lektion = MakeLektion();
        var plan    = Run([lektion], []);

        Assert.Empty(plan.Actions);
    }

    // -----------------------------------------------------------------------
    // Exam location enrichment from lesson
    // -----------------------------------------------------------------------

    [Fact]
    public void ExamWithNoRoom_EnrichedFromConcurrentLesson_HashIncludesRoom()
    {
        var exam    = MakePruefung(start: T0, location: null);
        var lesson  = MakeLektion(room: "039", start: T0);

        var planWith    = SyncEngine.Build([exam, lesson], [], DefaultOptions, FeedHealth.Healthy, T0.AddMinutes(-1));
        var createAction = planWith.Actions.Single(a => a.Kind == SyncActionKind.Create);

        // The action's Source should now have the room filled in.
        Assert.Equal("039", createAction.Source!.Location);
    }

    [Fact]
    public void ExamEnrichment_WhenDisabled_LocationRemainsNull()
    {
        var opts    = new SyncOptions { EnrichExamLocationFromLesson = false };
        var exam    = MakePruefung(start: T0, location: null);
        var lesson  = MakeLektion(room: "039", start: T0);

        var plan   = SyncEngine.Build([exam, lesson], [], opts, FeedHealth.Healthy, T0.AddMinutes(-1));
        var action = plan.Actions.Single(a => a.Kind == SyncActionKind.Create);

        Assert.Null(action.Source!.Location);
    }

    // -----------------------------------------------------------------------
    // Blockers
    // -----------------------------------------------------------------------

    [Fact]
    public void UnhealthyFeed_ProducesBlocker()
    {
        var badHealth = new FeedHealth(["Feed truncated"]);
        var plan = SyncEngine.Build([], [], DefaultOptions, badHealth, T0);
        Assert.False(plan.CanExecute);
        Assert.NotEmpty(plan.Blockers);
    }

    [Fact]
    public void EmptyFeedWithTrackedEvents_ProducesBlocker()
    {
        var tracked = Track(MakePruefung());
        // Feed completely empty → blocker B triggers.
        var plan = SyncEngine.Build([], [tracked], DefaultOptions, FeedHealth.Healthy,
            T0.AddMinutes(-1));
        Assert.False(plan.CanExecute);
    }

    [Fact]
    public void MassDelete_ExceedsThreshold_ProducesBlocker()
    {
        // Create 10 tracked Pruefungen, feed has none → 10 deletes > 5 AND > 20%.
        var tracked = Enumerable.Range(1, 10)
            .Select(i => Track(MakePruefung($"P_{i}"), missingSince: T0.AddHours(-48)))
            .ToList();

        var plan = Run([], tracked, now: T0.AddMinutes(-1));

        Assert.False(plan.CanExecute);
        Assert.Contains(plan.Blockers, b => b.Contains("Sicherheitsstopp"));
    }

    [Fact]
    public void Blockers_DoNotSuppressActionList_SoUiCanDisplayThem()
    {
        var tracked = Track(MakePruefung());
        var plan    = SyncEngine.Build([], [tracked], DefaultOptions, FeedHealth.Healthy,
            T0.AddMinutes(-1));

        // Blocked, but actions are still populated for dry-run display.
        Assert.False(plan.CanExecute);
        Assert.NotEmpty(plan.Actions);
    }

    // -----------------------------------------------------------------------
    // Duplikate — entstehen, wenn ein früherer Lauf bestehende Einträge
    // nicht erkannt und deshalb neu angelegt hat.
    // -----------------------------------------------------------------------

    /// <summary>Same key twice in the calendar: keep one, remove the surplus.</summary>
    [Fact]
    public void DuplicateTrackedKey_ProducesDeleteDuplicate_ForSurplusOnly()
    {
        var ev    = MakePruefung();
        var first  = Track(ev) with { CalendarEventId = "cal-1" };
        var second = Track(ev) with { CalendarEventId = "cal-2" };

        var plan = Run([ev], [first, second]);

        var dupes = plan.Actions
            .Where(a => a.Kind == SyncActionKind.DeleteDuplicate)
            .ToList();

        Assert.Single(dupes);
        Assert.Equal("cal-2", dupes[0].Existing!.CalendarEventId);

        // Der verbleibende Eintrag ist unverändert — kein Create, kein Update.
        Assert.DoesNotContain(plan.Actions, a => a.Kind == SyncActionKind.Create);
        Assert.DoesNotContain(plan.Actions, a => a.Kind == SyncActionKind.Update);
    }

    /// <summary>Three copies of the same key leave exactly one behind.</summary>
    [Fact]
    public void TripleTrackedKey_RemovesTwo()
    {
        var ev = MakePruefung();
        var copies = Enumerable.Range(1, 3)
            .Select(i => Track(ev) with { CalendarEventId = $"cal-{i}" })
            .ToList();

        var plan = Run([ev], copies);

        Assert.Equal(2, plan.DuplicateCount);
    }

    /// <summary>
    /// Removing duplicates must not trip the mass-deletion safeguard —
    /// it cleans up the app's own mistake, it is not a reaction to the feed.
    /// </summary>
    [Fact]
    public void ManyDuplicates_DoNotBlockThePlan()
    {
        var feed    = Enumerable.Range(1, 10).Select(i => MakePruefung($"P_{i}")).ToList();
        var tracked = new List<TrackedEvent>();
        foreach (var ev in feed)
        {
            tracked.Add(Track(ev) with { CalendarEventId = $"cal-{ev.Key}-a" });
            tracked.Add(Track(ev) with { CalendarEventId = $"cal-{ev.Key}-b" });
        }

        var plan = Run(feed, tracked);

        Assert.Equal(10, plan.DuplicateCount);
        Assert.Equal(0,  plan.DeleteCount);
        Assert.True(plan.CanExecute);
    }

    /// <summary>
    /// A duplicate of an event that vanished from the feed is removed once, and
    /// the remaining copy is flagged once — not twice.
    /// </summary>
    [Fact]
    public void DuplicateOfMissingEvent_IsNotFlaggedTwice()
    {
        var gone    = MakePruefung(start: T0.AddDays(3));
        var tracked = new[]
        {
            Track(gone) with { CalendarEventId = "cal-1" },
            Track(gone) with { CalendarEventId = "cal-2" },
        };

        // Der Feed umspannt den verschwundenen Termin (Regel 5a greift sonst
        // und überspringt alles ausserhalb des Feed-Fensters).
        var feed = new[]
        {
            MakePruefung("P_before", start: T0.AddDays(1)),
            MakePruefung("P_after",  start: T0.AddDays(5)),
        };

        var plan = Run(feed, tracked);

        Assert.Single(plan.Actions, a => a.Kind == SyncActionKind.DeleteDuplicate);
        Assert.Single(plan.Actions, a => a.Kind == SyncActionKind.FlagMissing);
    }

    // -----------------------------------------------------------------------
    // Manuelle Einträge — kommen aus der lokalen Liste, nicht aus dem Feed,
    // und werden darum unabhängig von den Typ-Schaltern synchronisiert.
    // -----------------------------------------------------------------------

    /// <summary>A hand-made event syncs even when its type is switched off.</summary>
    [Fact]
    public void ManualEvent_SyncsEvenWhenTypeDisabled()
    {
        var manual  = MakeManual(type: SchulnetzEventType.Termin);
        var options = new SyncOptions { EnabledTypes = new HashSet<SchulnetzEventType> { SchulnetzEventType.Pruefung } };

        var plan = Run([manual], [], options);

        Assert.Single(plan.Actions, a => a.Kind == SyncActionKind.Create);
    }

    /// <summary>Feed events still obey the type switches — only manual ones bypass them.</summary>
    [Fact]
    public void FeedEvent_StillRespectsDisabledType()
    {
        var options = new SyncOptions { EnabledTypes = new HashSet<SchulnetzEventType> { SchulnetzEventType.Pruefung } };

        var plan = Run([MakeTermin()], [], options);

        Assert.Empty(plan.Actions);
    }

    /// <summary>
    /// Deleted in the app → gone from the calendar right away: no grace period
    /// and no "[Abgesagt]" rename, unlike a feed event that vanished.
    /// </summary>
    [Fact]
    public void ManualEvent_RemovedFromList_IsDeletedImmediately()
    {
        var manual  = MakeManual(start: T0.AddDays(2));
        var tracked = Track(manual);

        // Der Benutzer hat ihn gelöscht → er ist nicht mehr in der Eingabe.
        var plan = Run([], [tracked]);

        var action = Assert.Single(plan.Actions);
        Assert.Equal(SyncActionKind.Delete, action.Kind);
        Assert.DoesNotContain(plan.Actions, a => a.Kind == SyncActionKind.FlagMissing);
        Assert.DoesNotContain(plan.Actions, a => a.Kind == SyncActionKind.MarkCancelled);
    }

    /// <summary>
    /// A manual exam is deleted outright, even though CancelInsteadOfDelete
    /// would rename a feed exam instead.
    /// </summary>
    [Fact]
    public void ManualPruefung_IsDeleted_NotMarkedCancelled()
    {
        var manual  = MakeManual(type: SchulnetzEventType.Pruefung, start: T0.AddDays(2));
        var options = new SyncOptions { CancelInsteadOfDelete = true };

        var plan = Run([], [Track(manual)], options);

        Assert.Single(plan.Actions, a => a.Kind == SyncActionKind.Delete);
    }

    /// <summary>Removal is not stopped by the feed window or the "past events" rule.</summary>
    [Fact]
    public void ManualEvent_InThePast_IsStillDeleted()
    {
        var manual = MakeManual(start: T0.AddDays(-30));

        var plan = Run([MakePruefung(start: T0.AddDays(3))], [Track(manual)]);

        Assert.Single(plan.Actions, a => a.Kind == SyncActionKind.Delete);
    }

    /// <summary>
    /// Deleting several manual events at once is a deliberate act and must not
    /// trip the safeguard meant for a broken feed.
    /// </summary>
    [Fact]
    public void ManyManualDeletions_DoNotBlockThePlan()
    {
        var tracked = Enumerable.Range(1, 20)
            .Select(i => Track(MakeManual($"Eigener {i}", start: T0.AddDays(i))))
            .ToList();

        var plan = Run([MakePruefung(start: T0.AddDays(3))], tracked);

        Assert.Equal(20, plan.DeleteCount);
        Assert.True(plan.CanExecute);
    }

    /// <summary>Manual entries alone must not raise the "feed looks empty" blocker.</summary>
    [Fact]
    public void ManualEntriesOnly_DoNotRaiseEmptyFeedBlocker()
    {
        var manual = MakeManual(type: SchulnetzEventType.Pruefung, start: T0.AddDays(2));

        var plan = Run([manual], [Track(manual)]);

        Assert.True(plan.CanExecute);
        Assert.Empty(plan.Blockers);
    }
}
