using SchulnetzSync.Core.Update;
using Xunit;

namespace SchulnetzSync.Tests.Update;

/// <summary>
/// The decisions around updates: whether the user gets asked, what postponing does, and
/// what happens when the check itself fails. No network, and the clock is a constant.
/// </summary>
public class UpdateDecisionTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 11, 8, 0, 0, TimeSpan.Zero);

    private static UpdateInfo Update(string version = "2.1.0.0", bool mandatory = false)
        => new(version, mandatory);

    // ── Stand-in store for the gate tests ────────────────────────────────────

    private sealed class StubSource(
        Func<CancellationToken, Task<IReadOnlyList<UpdateInfo>>> check) : IUpdateSource
    {
        public Task<IReadOnlyList<UpdateInfo>> CheckAsync(CancellationToken ct) => check(ct);
        public Task DownloadAsync(IProgress<double>? progress, CancellationToken ct) => Task.CompletedTask;
        public Task<InstallOutcome> InstallAsync(CancellationToken ct)
            => Task.FromResult(InstallOutcome.NeedsRestart);
    }

    private static UpdateGate GateReturning(params UpdateInfo[] updates)
        => new(new StubSource(_ => Task.FromResult<IReadOnlyList<UpdateInfo>>(updates)),
               TimeSpan.FromMilliseconds(2500));

    // ── Nothing on offer ─────────────────────────────────────────────────────

    [Fact]
    public async Task Kein_Update_vorhanden_startet_direkt()
    {
        var gate = GateReturning();

        var decision = await gate.EvaluateAsync(new UpdatePreferences(), Now, skipCheck: false);

        Assert.Equal(UpdatePrompt.None, decision.Prompt);
        Assert.Null(decision.FailureReason);
    }

    // ── Failures: the start must never hang on this ──────────────────────────

    [Fact]
    public async Task Pruefung_wirft_Exception_startet_direkt()
    {
        var gate = new UpdateGate(
            new StubSource(_ => throw new InvalidOperationException("kein Store-Kontext")),
            TimeSpan.FromMilliseconds(2500));

        var decision = await gate.EvaluateAsync(new UpdatePreferences(), Now, skipCheck: false);

        Assert.Equal(UpdatePrompt.None, decision.Prompt);
        Assert.Contains("kein Store-Kontext", decision.FailureReason);
    }

    [Fact]
    public async Task Pruefung_ueberschreitet_Timeout_startet_direkt()
    {
        // A source that ignores its cancellation token on purpose — the hard limit
        // has to bite even then.
        var gate = new UpdateGate(
            new StubSource(async _ =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30), CancellationToken.None);
                return Array.Empty<UpdateInfo>();
            }),
            TimeSpan.FromMilliseconds(100));

        var started  = DateTimeOffset.UtcNow;
        var decision = await gate.EvaluateAsync(new UpdatePreferences(), Now, skipCheck: false);
        var elapsed  = DateTimeOffset.UtcNow - started;

        Assert.Equal(UpdatePrompt.None, decision.Prompt);
        Assert.Contains("Zeitüberschreitung", decision.FailureReason);
        Assert.True(elapsed < TimeSpan.FromSeconds(5), $"Prüfung dauerte {elapsed.TotalSeconds:0.0} s");
    }

    // ── Restart after an install ─────────────────────────────────────────────

    [Fact]
    public async Task Start_mit_restarted_prueft_nicht()
    {
        bool asked = false;
        var gate = new UpdateGate(
            new StubSource(_ =>
            {
                asked = true;
                return Task.FromResult<IReadOnlyList<UpdateInfo>>([Update()]);
            }),
            TimeSpan.FromMilliseconds(2500));

        var decision = await gate.EvaluateAsync(new UpdatePreferences(), Now, skipCheck: true);

        Assert.False(asked, "Nach einem Neustart darf der Store nicht befragt werden.");
        Assert.Equal(UpdatePrompt.None, decision.Prompt);
    }

    // ── Ask, or stay out of the way ──────────────────────────────────────────

    [Fact]
    public void Update_noch_nie_verschoben_zeigt_Auswahl()
    {
        var decision = UpdatePolicy.Decide([Update()], new UpdatePreferences(), Now);

        Assert.Equal(UpdatePrompt.Optional, decision.Prompt);
        Assert.Equal("2.1.0.0", decision.Version);
    }

    [Fact]
    public void Innerhalb_der_Snooze_Frist_startet_direkt()
    {
        var prefs = new UpdatePreferences("2.1.0.0", Now.AddDays(2), 1);

        var decision = UpdatePolicy.Decide([Update()], prefs, Now);

        Assert.Equal(UpdatePrompt.None, decision.Prompt);
    }

    [Fact]
    public void Nach_Ablauf_der_Snooze_Frist_erscheint_Auswahl()
    {
        var prefs = new UpdatePreferences("2.1.0.0", Now.AddHours(-1), 1);

        var decision = UpdatePolicy.Decide([Update()], prefs, Now);

        Assert.Equal(UpdatePrompt.Optional, decision.Prompt);
    }

    [Fact]
    public void Zwingendes_Update_sticht_die_Snooze_Frist()
    {
        var prefs = new UpdatePreferences("2.1.0.0", Now.AddDays(30), 2);

        var decision = UpdatePolicy.Decide([Update(mandatory: true)], prefs, Now);

        Assert.Equal(UpdatePrompt.Mandatory, decision.Prompt);
    }

    [Fact]
    public void Neue_Version_waehrend_laufender_Snooze_fragt_wieder()
    {
        // 2.1.0.0 was pushed out 30 days; 2.2.0.0 is not covered by that.
        var prefs = new UpdatePreferences("2.1.0.0", Now.AddDays(30), 2);

        var decision = UpdatePolicy.Decide([Update("2.2.0.0")], prefs, Now);

        Assert.Equal(UpdatePrompt.Optional, decision.Prompt);
        Assert.Equal("2.2.0.0", decision.Version);
    }

    [Fact]
    public void Neue_Version_setzt_den_Zaehler_zurueck()
    {
        var prefs = new UpdatePreferences("2.1.0.0", Now.AddDays(30), 2);

        var next = UpdatePolicy.Postpone(prefs, "2.2.0.0", Now);

        Assert.Equal(1, next.PostponeCount);
        Assert.Equal("2.2.0.0", next.SkippedVersion);
        Assert.Equal(Now.AddDays(1), next.RemindAfterUtc);
    }

    // ── How long a postponement lasts ────────────────────────────────────────

    [Fact]
    public void Verschieben_erinnert_am_naechsten_Tag()
    {
        var next = UpdatePolicy.Postpone(new UpdatePreferences(), "2.1.0.0", Now);

        Assert.Equal(1, next.PostponeCount);
        Assert.Equal(Now.AddDays(1), next.RemindAfterUtc);
    }

    [Fact]
    public void Jedes_weitere_Verschieben_kostet_ebenfalls_einen_Tag()
    {
        // The wait does not grow with the number of postponements.
        var prefs = new UpdatePreferences("2.1.0.0", Now.AddDays(-1), 4);

        var next = UpdatePolicy.Postpone(prefs, "2.1.0.0", Now);

        Assert.Equal(5, next.PostponeCount);
        Assert.Equal(Now.AddDays(1), next.RemindAfterUtc);
    }

    [Fact]
    public void Nach_einem_Tag_erscheint_die_Auswahl_wieder()
    {
        var postponed = UpdatePolicy.Postpone(new UpdatePreferences(), "2.1.0.0", Now);

        // Just before: still quiet.
        Assert.Equal(UpdatePrompt.None,
            UpdatePolicy.Decide([Update()], postponed, Now.AddHours(23)).Prompt);

        // Just after: ask again.
        Assert.Equal(UpdatePrompt.Optional,
            UpdatePolicy.Decide([Update()], postponed, Now.AddHours(25)).Prompt);
    }

    // ── Urgent always gets through ───────────────────────────────────────────

    [Fact]
    public async Task Waehrend_der_Snooze_wird_trotzdem_beim_Store_geprueft()
    {
        // A postponement may silence the question, never the check itself —
        // otherwise a mandatory update would sit there unnoticed.
        bool asked = false;
        var gate = new UpdateGate(
            new StubSource(_ =>
            {
                asked = true;
                return Task.FromResult<IReadOnlyList<UpdateInfo>>([Update()]);
            }),
            TimeSpan.FromMilliseconds(2500));

        var snoozed = new UpdatePreferences("2.1.0.0", Now.AddDays(1), 1);

        var decision = await gate.EvaluateAsync(snoozed, Now, skipCheck: false);

        Assert.True(asked, "Auch waehrend der Frist muss der Store befragt werden.");
        Assert.Equal(UpdatePrompt.None, decision.Prompt);
    }

    [Fact]
    public async Task Dringendes_Update_erscheint_auch_waehrend_der_Snooze()
    {
        var gate = GateReturning(Update(mandatory: true));
        var snoozed = new UpdatePreferences("2.1.0.0", Now.AddDays(1), 1);

        var decision = await gate.EvaluateAsync(snoozed, Now, skipCheck: false);

        Assert.Equal(UpdatePrompt.Mandatory, decision.Prompt);
    }

    // ── Comparing versions for the message ──────────────────────────────────

    [Theory]
    [InlineData("2.2.0.0", "2.1.0", true)]
    [InlineData("2.2.0",   "2.1.0", true)]
    [InlineData("3.0.0.0", "2.9.9", true)]
    [InlineData("2.1.0.0", "2.1.0", false)]   // the store naming the version already installed
    [InlineData("2.0.0.0", "2.1.0", false)]
    [InlineData(null,      "2.1.0", false)]
    [InlineData("",        "2.1.0", false)]
    [InlineData("neu",     "2.1.0", false)]
    public void IsNewerThan_OnlyHigherVersionsCount(string? offered, string current, bool expected)
        => Assert.Equal(expected, UpdatePolicy.IsNewerThan(offered, current));
}
