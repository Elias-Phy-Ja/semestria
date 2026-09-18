namespace SchulnetzSync.Core.Update;

/// <summary>
/// Runs the update check under a hard time limit and turns every failure into
/// "just start the app".
///
/// The start must never hang on the check: offline, no store context when sideloaded,
/// a WinRT call that never answers — in all of those the app carries on as usual. The
/// reason travels along in <see cref="UpdateDecision.FailureReason"/> so the caller can
/// log it. Nothing is swallowed silently.
/// </summary>
public sealed class UpdateGate(IUpdateSource source, TimeSpan timeout)
{
    private readonly IUpdateSource _source  = source;
    private readonly TimeSpan      _timeout = timeout;

    /// <summary>Checks for updates and applies the snooze rules.</summary>
    /// <param name="prefs">Snooze state from disk.</param>
    /// <param name="nowUtc">Current time, injected so the tests stay deterministic.</param>
    /// <param name="skipCheck">
    /// True right after an update installed itself (start with <c>/restarted</c>):
    /// asking the store again would find nothing and only delay the start.
    /// </param>
    /// <param name="ct">Cancels the whole thing, e.g. when the app closes.</param>
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

            // Racing a delay instead of just calling CancelAfter: a source that ignores
            // its token could otherwise hold up the start for as long as it likes.
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
            // The app is shutting down — that is not a failed check, so let it through.
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
