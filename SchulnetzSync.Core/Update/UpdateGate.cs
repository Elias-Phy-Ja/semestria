namespace SchulnetzSync.Core.Update;

/// <summary>
/// Runs the update check under a hard time limit and turns every failure into
/// "just start the app".
///
/// Der Start darf nie an der Prüfung hängen: offline, kein Store-Bezug beim
/// Sideload, eine hängende WinRT-Antwort — in allen Fällen läuft die App
/// normal weiter. Der Grund geht als <see cref="UpdateDecision.FailureReason"/>
/// mit, damit der Aufrufer ihn protokollieren kann. Verschluckt wird nichts.
/// </summary>
public sealed class UpdateGate(IUpdateSource source, TimeSpan timeout)
{
    private readonly IUpdateSource _source  = source;
    private readonly TimeSpan      _timeout = timeout;

    /// <summary>
    /// Checks for updates and applies the snooze rules.
    /// </summary>
    /// <param name="prefs">Snooze state from disk.</param>
    /// <param name="nowUtc">Current time, injected for deterministic tests.</param>
    /// <param name="skipCheck">
    /// True right after an update installed itself (start with <c>/restarted</c>):
    /// asking the store again would be pointless and would only delay the start.
    /// </param>
    /// <param name="ct">Cancels the whole operation, e.g. when the app closes.</param>
    public async Task<UpdateDecision> EvaluateAsync(
        UpdatePreferences prefs,
        DateTimeOffset    nowUtc,
        bool              skipCheck,
        CancellationToken ct = default)
    {
        if (skipCheck)
            return new UpdateDecision(UpdatePrompt.None);

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);

        try
        {
            var check = _source.CheckAsync(limit.Token);

            // Das Rennen gegen eine Verzögerung statt nur CancelAfter: Eine
            // Quelle, die ihren Token nicht beachtet, könnte den Start sonst
            // beliebig lange aufhalten.
            var finished = await Task.WhenAny(check, Task.Delay(_timeout, ct))
                                     .ConfigureAwait(false);

            if (finished != check)
            {
                limit.Cancel();
                return new UpdateDecision(
                    UpdatePrompt.None,
                    FailureReason: $"Zeitüberschreitung nach {_timeout.TotalMilliseconds:0} ms");
            }

            var updates = await check.ConfigureAwait(false);
            return UpdatePolicy.Decide(updates, prefs, nowUtc);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Die App fährt herunter — das ist kein Fehlerfall der Prüfung.
            throw;
        }
        catch (OperationCanceledException)
        {
            return new UpdateDecision(
                UpdatePrompt.None,
                FailureReason: $"Prüfung abgebrochen nach {_timeout.TotalMilliseconds:0} ms");
        }
        catch (Exception ex)
        {
            return new UpdateDecision(
                UpdatePrompt.None,
                FailureReason: $"{ex.GetType().Name}: {ex.Message}");
        }
    }
}
