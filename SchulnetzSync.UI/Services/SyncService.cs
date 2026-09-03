using SchulnetzSync.Core.Calendar;
using SchulnetzSync.Core.Configuration;
using SchulnetzSync.Core.Feed;
using SchulnetzSync.Core.Model;
using SchulnetzSync.Core.Sync;

namespace SchulnetzSync.UI.Services;

/// <summary>
/// Kapselt die vollständige Sync-Logik für alle UI-Seiten.
/// Läuft auf dem Thread-Pool; meldet Fortschritt via IProgress und Events.
/// </summary>
public sealed class SyncService
{
    public bool IsSyncing { get; private set; }

    public event Action<string>?      ProgressReceived;
    public event Action<SyncResult>?  Completed;
    public event Action<Exception>?   Failed;

    public async Task RunAsync(bool dryRun, CancellationToken ct = default)
    {
        if (IsSyncing) return;
        IsSyncing = true;
        AppState.IsSyncing = true;
        AppState.Notify();

        try
        {
            var result = await Task.Run(() => CoreSyncAsync(dryRun, ct), ct);
            Completed?.Invoke(result);

            if (!dryRun)
            {
                AppState.Config.LastRunAt     = result.Timestamp;
                AppState.Config.LastRunResult = result.Summary;
                ConfigManager.Save(AppState.Config);
                AppState.Notify();
            }
        }
        catch (OperationCanceledException)
        {
            Report("Abgebrochen.");
        }
        catch (Exception ex)
        {
            // Bereinige Exception-Nachricht: URL-Token darf nie in der UI erscheinen
            Failed?.Invoke(new Exception(SanitizeException(ex)));
        }
        finally
        {
            IsSyncing = false;
            AppState.IsSyncing = false;
            AppState.Notify();
        }
    }

    // -----------------------------------------------------------------------

    private async Task<SyncResult> CoreSyncAsync(bool dryRun, CancellationToken ct)
    {
        var config   = AppState.Config;
        var clientId = config.ClientId ?? AppConstants.ClientId;

        // Freundliche Fehlermeldung bevor wir MSAL aufrufen — verhindert den
        // «YOUR-CLIENT-ID-HERE»-Fehler von Microsoft im Browser
        if (!IsValidClientId(clientId))
            throw new InvalidOperationException(
                "Kein Microsoft-Konto konfiguriert. Gehe zu Einstellungen um dich anzumelden.");

        var plainUrl = ConfigManager.GetFeedUrl(config)
            ?? throw new InvalidOperationException(
                "Keine Feed-URL konfiguriert. Bitte Einstellungen öffnen.");

        Report("⏳ Feed wird geladen…");
        using var http   = new HttpClient();
        var source       = new HttpFeedSource(http, plainUrl);
        var icsContent   = await source.FetchAsync(ct);
        var feedHealth   = FeedParser.CheckPlausibility(icsContent);
        var feedEvents   = FeedParser.Parse(icsContent);

        // Feed-Events cachen — EventsPage braucht das immer (auch bei Dry-Run)
        AppState.CachedFeedEvents = feedEvents;
        AppState.Notify();

        Report($"✅ {feedEvents.Count} Einträge im Feed.");

        IReadOnlyList<TrackedEvent> tracked = [];
        GraphCalendarTarget? calendar = null;

        if (!dryRun)
        {
            Report("🔐 Microsoft-Anmeldung…");
            var auth  = new MsalAuthProvider(clientId);
            string token;
            try   { token = await auth.AcquireTokenSilentAsync(ct); }
            catch (InteractiveLoginRequiredException)
            { token = await auth.AcquireTokenInteractiveAsync(ct); }

            calendar = new GraphCalendarTarget(token);
            var options = config.ToSyncOptions();
            var from    = feedEvents.Count > 0 ? feedEvents.Min(e => e.Start).AddDays(-1) : DateTimeOffset.UtcNow;
            var to      = feedEvents.Count > 0 ? feedEvents.Max(e => e.Start).AddDays(1)  : DateTimeOffset.UtcNow.AddYears(1);
            tracked     = await calendar.GetTrackedEventsAsync(from, to, options.CalendarId, ct);
            Report($"📅 {tracked.Count} bestehende Einträge im Kalender.");
        }

        var plan = SyncEngine.Build(feedEvents, tracked, config.ToSyncOptions(), feedHealth, DateTimeOffset.Now);

        if (!plan.CanExecute)
            throw new InvalidOperationException(
                "Plan blockiert:\n" + string.Join("\n", plan.Blockers));

        if (!dryRun && plan.Actions.Count > 0)
        {
            Report("✏️  Synchronisation läuft…");
            await calendar!.ExecutePlanAsync(plan, config.ToSyncOptions(),
                new Progress<string>(msg => Report("  " + msg)), ct);
        }

        var summary = plan.Actions.Count == 0
            ? "Alles aktuell. Nichts zu tun."
            : $"{plan.CreateCount} neu,  {plan.UpdateCount} aktualisiert,  {plan.DeleteCount} gelöscht";

        Report(dryRun ? $"👁  Vorschau: {summary}" : $"✅  {summary}");

        return new SyncResult(
            Timestamp:    DateTimeOffset.UtcNow,
            Summary:      summary,
            IsDryRun:     dryRun,
            CreateCount:  plan.CreateCount,
            UpdateCount:  plan.UpdateCount,
            DeleteCount:  plan.DeleteCount,
            FeedCount:    feedEvents.Count,
            Plan:         plan);
    }

    private void Report(string msg)
    {
        AppState.SyncStatus = msg;
        ProgressReceived?.Invoke(msg);
    }

    /// <summary>
    /// Entfernt URLs (und damit Feed-Tokens) aus Exception-Nachrichten,
    /// bevor sie in der UI angezeigt werden.
    /// </summary>
    private static string SanitizeException(Exception ex)
    {
        var msg = ex.InnerException?.Message ?? ex.Message;
        msg = System.Text.RegularExpressions.Regex.Replace(
            msg, @"https?://\S+", "[URL ausgeblendet]");
        msg = System.Text.RegularExpressions.Regex.Replace(
            msg, @"webcal://\S+", "[URL ausgeblendet]");
        var first = msg.Split('\n')[0].Trim();
        return first.Length > 200 ? first[..200] + "…" : first;
    }

    private static bool IsValidClientId(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        if (id.Contains("YOUR", StringComparison.OrdinalIgnoreCase)) return false;
        return id.Length >= 32 && id.Contains('-');
    }
}

/// <summary>Ergebnis eines abgeschlossenen Sync-Laufs.</summary>
public sealed record SyncResult(
    DateTimeOffset          Timestamp,
    string                  Summary,
    bool                    IsDryRun,
    int                     CreateCount,
    int                     UpdateCount,
    int                     DeleteCount,
    int                     FeedCount,
    SchulnetzSync.Core.Sync.SyncPlan Plan);
