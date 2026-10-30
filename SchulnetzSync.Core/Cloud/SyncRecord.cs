using System.Security.Cryptography;
using System.Text;

namespace SchulnetzSync.Core.Cloud;

/// <summary>What a record holds.</summary>
public enum SyncRecordKind
{
    Task,
    TaskList,
    SubjectComment,
    ManualPruefung,
    Pruefung,
    PruefungOverlay,
    SubjectList
}

public static class SyncRecordKinds
{
    /// <summary>
    /// True for what only the Store app produces. A different version in the cloud is never
    /// taken over, it gets overwritten.
    /// </summary>
    public static bool IsStoreOwned(this SyncRecordKind kind)
        => kind is SyncRecordKind.Pruefung or SyncRecordKind.SubjectList;
}

/// <summary>One record as it exists on this device.</summary>
/// <param name="Id">Stable identity. The encryption layer turns it into the document id.</param>
/// <param name="Kind">What it holds.</param>
/// <param name="Content">The record as JSON.</param>
public sealed record SyncRecord(string Id, SyncRecordKind Kind, string Content)
{
    /// <summary>Fingerprint of the content, used to notice changes.</summary>
    public string Hash { get; } = HashOf(Content);

    public static string HashOf(string content)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
}

/// <summary>Points at a record without its content.</summary>
public sealed record SyncRef(string Id, SyncRecordKind Kind);

/// <summary>A record as it came back from the cloud, already decrypted.</summary>
/// <param name="Id">Stable identity.</param>
/// <param name="Kind">What it holds.</param>
/// <param name="Content">The record as JSON, null for a deleted one.</param>
/// <param name="Deleted">True for a tombstone.</param>
/// <param name="UpdatedAt">When the writing device changed it. For display only.</param>
/// <param name="ServerTime">
/// When the server received it. Decides what the next sync fetches; a device clock could
/// not, because an edit made offline would carry an old time and be missed.
/// </param>
public sealed record RemoteRecord(
    string Id,
    SyncRecordKind Kind,
    string? Content,
    bool Deleted,
    DateTimeOffset UpdatedAt,
    DateTimeOffset ServerTime);

/// <summary>
/// The last state this device and the cloud agreed on for one record. Comparing it with the
/// present is how a change shows up, so the data files themselves need no sync fields, and
/// a record that is gone from the data but still listed here was deleted on this device.
/// </summary>
/// <param name="Id">Stable identity.</param>
/// <param name="Kind">What it holds.</param>
/// <param name="Hash">Fingerprint of the agreed content, empty for a deleted record.</param>
/// <param name="UpdatedAt">When that state came about. For display only.</param>
/// <param name="DeletedAt">When the record was deleted, null while it exists.</param>
public sealed record SyncLedgerEntry(
    string Id,
    SyncRecordKind Kind,
    string Hash,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? DeletedAt);
