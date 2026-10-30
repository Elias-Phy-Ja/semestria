namespace SchulnetzSync.Core.Cloud;

/// <summary>What has to happen to bring this device and the cloud together.</summary>
/// <param name="Uploads">Records to write to the cloud.</param>
/// <param name="Tombstones">Records to mark as deleted in the cloud.</param>
/// <param name="ApplyLocally">Records to take over from the cloud.</param>
/// <param name="RemoveLocally">Records deleted in the cloud.</param>
/// <param name="PurgeRemote">Old tombstones to remove from the cloud for good.</param>
/// <param name="Ledger">
/// The ledger after this plan. Save it only once every action succeeded: a run that fails
/// half way then simply starts over and arrives at the same plan.
/// </param>
/// <param name="Conflicts">Records changed on both sides; the Store app's version won.</param>
/// <param name="Cursor">Server time to fetch from next time.</param>
public sealed record SyncMergePlan(
    IReadOnlyList<SyncRecord>      Uploads,
    IReadOnlyList<SyncRef>         Tombstones,
    IReadOnlyList<SyncRecord>      ApplyLocally,
    IReadOnlyList<SyncRef>         RemoveLocally,
    IReadOnlyList<SyncRef>         PurgeRemote,
    IReadOnlyList<SyncLedgerEntry> Ledger,
    int                            Conflicts,
    DateTimeOffset?                Cursor);

/// <summary>
/// The merge: local records, ledger and cloud changes in, action plan out. A pure function
/// like the calendar diff (no network, no clock, no files).
///
/// The Store app has the highest authority. A change made only in the cloud is taken over;
/// when both sides changed a record, the Store app's version wins. No clock is involved, so
/// a wrong time on a device cannot decide a conflict.
/// </summary>
public static class SyncMerger
{
    /// <summary>How long a tombstone stays, so that every device gets to see it.</summary>
    public static readonly TimeSpan TombstoneLifetime = TimeSpan.FromDays(90);

    /// <param name="local">Every record on this device.</param>
    /// <param name="ledger">The state agreed on last time, empty on the first sync.</param>
    /// <param name="remote">What changed in the cloud since <paramref name="cursor"/>.</param>
    /// <param name="cursor">Server time of the last fetch, null on the first sync.</param>
    /// <param name="now">Current time, injected so the tests stay deterministic.</param>
    public static SyncMergePlan Merge(
        IReadOnlyList<SyncRecord>      local,
        IReadOnlyList<SyncLedgerEntry> ledger,
        IReadOnlyList<RemoteRecord>    remote,
        DateTimeOffset?                cursor,
        DateTimeOffset                 now)
    {
        var mine   = local.ToDictionary(r => r.Id, StringComparer.Ordinal);
        var agreed = ledger.ToDictionary(e => e.Id, StringComparer.Ordinal);
        var theirs = remote.ToDictionary(r => r.Id, StringComparer.Ordinal);

        var uploads    = new List<SyncRecord>();
        var tombstones = new List<SyncRef>();
        var apply      = new List<SyncRecord>();
        var remove     = new List<SyncRef>();
        var purge      = new List<SyncRef>();
        var newLedger  = new List<SyncLedgerEntry>();
        int conflicts  = 0;

        var ids = mine.Keys.Concat(agreed.Keys).Concat(theirs.Keys)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal);

        foreach (var id in ids)
        {
            mine.TryGetValue(id, out var l);
            agreed.TryGetValue(id, out var b);
            theirs.TryGetValue(id, out var r);

            var kind         = l?.Kind ?? r?.Kind ?? b!.Kind;
            var exists       = b is { DeletedAt: null };
            var localChanged = l is null ? exists : l.Hash != b?.Hash;

            void Push()
            {
                if (l is not null)
                {
                    uploads.Add(l);
                    newLedger.Add(new SyncLedgerEntry(id, kind, l.Hash, now, null));
                }
                else
                {
                    tombstones.Add(new SyncRef(id, kind));
                    newLedger.Add(Deleted(id, kind, now));
                }
            }

            if (r is null)
            {
                if (localChanged)
                {
                    Push();
                }
                else if (b is { DeletedAt: { } deletedAt } && now - deletedAt >= TombstoneLifetime)
                {
                    purge.Add(new SyncRef(id, kind));
                }
                else if (b is not null)
                {
                    newLedger.Add(b);
                }
                continue;
            }

            if (!r.Deleted && r.Content is null)
                throw new InvalidDataException($"Datensatz {id} aus der Cloud hat keinen Inhalt.");

            var remoteHash = r.Deleted ? string.Empty : SyncRecord.HashOf(r.Content!);

            if (l is not null && !r.Deleted && l.Hash == remoteHash)
            {
                newLedger.Add(new SyncLedgerEntry(id, kind, l.Hash, r.UpdatedAt, null));
            }
            else if (l is null && r.Deleted)
            {
                newLedger.Add(Deleted(id, kind, b?.DeletedAt ?? now));
            }
            else if (localChanged || kind.IsStoreOwned())
            {
                if (localChanged) conflicts++;
                Push();
            }
            else if (r.Deleted)
            {
                remove.Add(new SyncRef(id, kind));
                newLedger.Add(Deleted(id, kind, now));
            }
            else
            {
                apply.Add(new SyncRecord(id, kind, r.Content!));
                newLedger.Add(new SyncLedgerEntry(id, kind, remoteHash, r.UpdatedAt, null));
            }
        }

        var newCursor = remote
            .Select(r => (DateTimeOffset?)r.ServerTime)
            .Append(cursor)
            .Max();

        return new SyncMergePlan(uploads, tombstones, apply, remove, purge, newLedger, conflicts, newCursor);
    }

    private static SyncLedgerEntry Deleted(string id, SyncRecordKind kind, DateTimeOffset at)
        => new(id, kind, string.Empty, at, at);
}
