using System.CommandLine;
using System.CommandLine.Invocation;
using SchulnetzSync.Core.Calendar;
using SchulnetzSync.Core.Configuration;
using SchulnetzSync.Core.Feed;
using SchulnetzSync.Core.Model;
using SchulnetzSync.Core.Sync;

// Command line entry point. Mostly here for scheduled runs and for debugging the
// sync without the UI in the way.
//
// Exit codes: 0 = done, 1 = error, 2 = plan was blocked, 3 = interactive login needed.
// The Task Scheduler reads those, so do not renumber them.

var rootCmd = new RootCommand("SchulnetzSync — Schulnetz → Outlook Kalender");

// Everything the CLI understands. --silent is the one the scheduled task uses.
var dryRunOpt  = new Option<bool>("--dry-run",  "Plan berechnen und anzeigen, nichts schreiben");
var syncOpt    = new Option<bool>("--sync",     "Plan berechnen und ausführen");
var silentOpt  = new Option<bool>("--silent",   "Wie --sync, ohne Ausgabe, ohne interaktiven Login");
var loginOpt   = new Option<bool>("--login",    "Einmalig interaktiv anmelden");
var typesOpt   = new Option<string?>("--types", "Typen für diesen Lauf (pruefung,termin)");
var purgeOpt   = new Option<string?>("--purge", "Alle Einträge dieses Typs löschen");
var confirmOpt = new Option<bool>("--confirm",  "Bestätigung für --purge");
var feedOpt    = new Option<string?>("--feed",  "Feed-URL überschreiben (für Tests)");

rootCmd.AddOption(dryRunOpt);
rootCmd.AddOption(syncOpt);
rootCmd.AddOption(silentOpt);
rootCmd.AddOption(loginOpt);
rootCmd.AddOption(typesOpt);
rootCmd.AddOption(purgeOpt);
rootCmd.AddOption(confirmOpt);
rootCmd.AddOption(feedOpt);

rootCmd.SetHandler(async ctx =>
{
    bool dryRun  = ctx.ParseResult.GetValueForOption(dryRunOpt);
    bool sync    = ctx.ParseResult.GetValueForOption(syncOpt);
    bool silent  = ctx.ParseResult.GetValueForOption(silentOpt);
    bool login   = ctx.ParseResult.GetValueForOption(loginOpt);
    string? types   = ctx.ParseResult.GetValueForOption(typesOpt);
    string? purge   = ctx.ParseResult.GetValueForOption(purgeOpt);
    bool confirm = ctx.ParseResult.GetValueForOption(confirmOpt);
    string? feedUrl = ctx.ParseResult.GetValueForOption(feedOpt);

    var config = ConfigManager.Load();

    // --login: sign in and nothing else, so the silent runs afterwards have a token.
    if (login)
    {
        if (config.ClientId is null)
        {
            Console.Error.WriteLine("Fehler: Keine Client-ID konfiguriert.");
            ctx.ExitCode = 1; return;
        }
        var auth = new MsalAuthProvider(config.ClientId);
        await auth.AcquireTokenInteractiveAsync(ctx.GetCancellationToken());
        Console.WriteLine("Anmeldung erfolgreich.");
        ctx.ExitCode = 0; return;
    }

    // --purge: bulk delete, guarded by --confirm because there is no undo.
    if (purge is not null)
    {
        if (!confirm)
        {
            Console.Error.WriteLine("Bitte --confirm hinzufügen um --purge auszuführen.");
            ctx.ExitCode = 1; return;
        }
        if (!Enum.TryParse<SchulnetzEventType>(purge, ignoreCase: true, out var purgeType)
            || purgeType == SchulnetzEventType.Lektion)
        {
            Console.Error.WriteLine($"Ungültiger Typ: {purge}. Erlaubt: pruefung, termin");
            ctx.ExitCode = 1; return;
        }

        var token   = await GetTokenAsync(config, silent, ctx); if (ctx.ExitCode != 0) return;
        var target  = new GraphCalendarTarget(token!);
        Console.WriteLine($"Lösche alle {purgeType}-Einträge...");
        var purged = await target.PurgeAsync(purgeType, config.CalendarId,
            new Progress<string>(Console.WriteLine), ctx.GetCancellationToken());
        Console.WriteLine($"Fertig. {purged} Einträge gelöscht.");
        ctx.ExitCode = 0; return;
    }

    // No mode given at all — print the usage instead of guessing what was meant.
    if (!dryRun && !sync && !silent)
    {
        Console.WriteLine(rootCmd.Description);
        Console.WriteLine("Verwendung: schnz --dry-run | --sync | --silent | --login");
        ctx.ExitCode = 0; return;
    }

    // --feed wins over the stored URL, which is how tests run against a local file.
    string? plainUrl = feedUrl ?? ConfigManager.GetFeedUrl(config);
    if (plainUrl is null)
    {
        Console.Error.WriteLine("Fehler: Keine Feed-URL konfiguriert. Starte die App und trage die URL ein.");
        ctx.ExitCode = 1; return;
    }

    // --types narrows the run without touching the saved settings.
    var options = config.ToSyncOptions();
    if (types is not null)
    {
        var enabledTypes = new HashSet<SchulnetzEventType>();
        foreach (var t in types.Split(','))
        {
            if (Enum.TryParse<SchulnetzEventType>(t.Trim(), ignoreCase: true, out var et)
                && et != SchulnetzEventType.Lektion)
                enabledTypes.Add(et);
        }
        if (enabledTypes.Count > 0)
            options = new SyncOptions
            {
                EnabledTypes                 = enabledTypes,
                CalendarId                   = options.CalendarId,
                CancelInsteadOfDelete        = options.CancelInsteadOfDelete,
                EnrichExamLocationFromLesson = options.EnrichExamLocationFromLesson,
            };
    }

    // Fetch and parse the feed.
    using var http       = new HttpClient();
    var feedSource       = new HttpFeedSource(http, plainUrl);
    string icsContent;
    try
    {
        icsContent = await feedSource.FetchAsync(ctx.GetCancellationToken());
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Feed-Fehler: {ex.Message}");
        ctx.ExitCode = 1; return;
    }

    var feedHealth = FeedParser.CheckPlausibility(icsContent);
    var feedEvents = FeedParser.Parse(icsContent);

    // A dry run stays offline on purpose: no token needed, so it works on any machine.
    IReadOnlyList<TrackedEvent> tracked = [];
    if (!dryRun)
    {
        if (config.ClientId is null)
        {
            Console.Error.WriteLine("Fehler: Keine Client-ID konfiguriert.");
            ctx.ExitCode = 1; return;
        }
        var token = await GetTokenAsync(config, silent, ctx); if (ctx.ExitCode != 0) return;
        var target = new GraphCalendarTarget(token!);

        var from = feedEvents.Count > 0 ? feedEvents.Min(e => e.Start).AddDays(-1) : DateTimeOffset.UtcNow;
        var to   = feedEvents.Count > 0 ? feedEvents.Max(e => e.Start).AddDays(1)  : DateTimeOffset.UtcNow.AddYears(1);
        tracked  = await target.GetTrackedEventsAsync(
            from, to, options.CalendarId,
            new Progress<string>(Console.WriteLine), ctx.GetCancellationToken());
    }

    // The diff itself. Everything above was only gathering its inputs.
    var plan = SyncEngine.Build(feedEvents, tracked, options, feedHealth, DateTimeOffset.Now);

    if (!silent)
        PrintPlan(plan);

    if (dryRun)
    {
        ctx.ExitCode = plan.CanExecute ? 0 : 2; return;
    }

    // From here on the calendar actually gets written to.
    if (!plan.CanExecute)
    {
        if (!silent)
            foreach (var b in plan.Blockers)
                Console.Error.WriteLine($"BLOCKIERT: {b}");
        ctx.ExitCode = 2; return;
    }

    if (plan.Actions.Count == 0)
    {
        if (!silent) Console.WriteLine("Alles aktuell. Nichts zu tun.");
        ctx.ExitCode = 0; return;
    }

    var token2   = await GetTokenAsync(config, silent, ctx); if (ctx.ExitCode != 0) return;
    var calendar = new GraphCalendarTarget(token2!);
    var progress = silent ? null : new Progress<string>(Console.WriteLine);
    await calendar.ExecutePlanAsync(plan, options, progress, ctx.GetCancellationToken());

    config.LastRunAt     = DateTimeOffset.UtcNow;
    config.LastRunResult = $"{plan.CreateCount} neu, {plan.UpdateCount} aktualisiert, {plan.DeleteCount} gelöscht";
    ConfigManager.Save(config);

    if (!silent)
        Console.WriteLine($"Fertig: {config.LastRunResult}");

    ctx.ExitCode = 0;
});

return await rootCmd.InvokeAsync(args);

// ── Helpers ─────────────────────────────────────────────────────────────

static async Task<string?> GetTokenAsync(
    SyncConfig config, bool silent, InvocationContext ctx)
{
    if (config.ClientId is null)
    {
        Console.Error.WriteLine("Fehler: Keine Client-ID konfiguriert.");
        ctx.ExitCode = 1; return null;
    }

    var auth = new MsalAuthProvider(config.ClientId);
    try
    {
        return await auth.AcquireTokenSilentAsync(ctx.GetCancellationToken());
    }
    catch (InteractiveLoginRequiredException)
    {
        if (silent)
        {
            // Silent means silent: a scheduled run must never pop a browser window
            // at someone. Exit code 3 tells the caller a login is due.
            ctx.ExitCode = 3; return null;
        }
        return await auth.AcquireTokenInteractiveAsync(ctx.GetCancellationToken());
    }
}

static void PrintPlan(SyncPlan plan)
{
    Console.WriteLine($"\n── Sync-Plan ──────────────────────────────");
    Console.WriteLine($"  Neu:          {plan.CreateCount}");
    Console.WriteLine($"  Aktualisiert: {plan.UpdateCount}");
    Console.WriteLine($"  Gelöscht:     {plan.DeleteCount}");
    Console.WriteLine($"  Fehlt (neu):  {plan.FlagCount}");
    Console.WriteLine($"  Zurück:       {plan.ClearCount}");

    if (!plan.CanExecute)
    {
        Console.WriteLine("\n  ⚠ Blockiert:");
        foreach (var b in plan.Blockers)
            Console.WriteLine($"    • {b}");
    }

    Console.WriteLine("\n  Aktionen:");
    foreach (var a in plan.Actions)
    {
        string label = a.Kind switch
        {
            SyncActionKind.Create       => "  + NEU     ",
            SyncActionKind.Update       => "  ~ UPDATE  ",
            SyncActionKind.Delete       => "  - LÖSCHEN ",
            SyncActionKind.MarkCancelled => "  x ABSAGEN ",
            SyncActionKind.FlagMissing  => "  ? FEHLT   ",
            SyncActionKind.ClearMissing => "  ✓ ZURÜCK  ",
            _                           => "  ? "
        };
        string title = a.Source?.Summary ?? a.Existing?.Key ?? "?";
        string date  = (a.Source?.Start ?? a.Existing?.Start)?.ToString("dd.MM.yyyy HH:mm") ?? "";
        Console.WriteLine($"{label} {title}  [{date}]");
    }
    Console.WriteLine("────────────────────────────────────────────\n");
}
