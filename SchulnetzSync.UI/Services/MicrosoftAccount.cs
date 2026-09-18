using SchulnetzSync.Core.Configuration;

namespace SchulnetzSync.UI.Services;

/// <summary>
/// Works out which Entra application id to sign in with.
///
/// Normally the app brings its own registration (<see cref="AppConstants.ClientId"/>), so
/// the user just signs in and never sees an Azure portal. The field in the settings is
/// there for the rare person who wants to point the app at their own registration.
/// </summary>
public static class MicrosoftAccount
{
    /// <summary>True when the string looks like a real GUID rather than a leftover placeholder.</summary>
    public static bool IsUsable(string? id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        if (id.Contains("YOUR",        StringComparison.OrdinalIgnoreCase)) return false;
        if (id.Contains("PLACEHOLDER", StringComparison.OrdinalIgnoreCase)) return false;
        return id.Length >= 32 && id.Contains('-');
    }

    /// <summary>True when this build ships its own app registration.</summary>
    public static bool HasBuiltInId => IsUsable(AppConstants.ClientId);

    /// <summary>
    /// The id to sign in with. A user-supplied one wins over the built-in one; null when
    /// neither is usable, which is the case where Outlook sync simply is not on offer.
    /// </summary>
    public static string? Resolve(SyncConfig config)
    {
        var custom = config.ClientId?.Trim();
        if (IsUsable(custom)) return custom;
        return HasBuiltInId ? AppConstants.ClientId : null;
    }

    /// <summary>True when a sign-in can be attempted at all.</summary>
    public static bool IsAvailable(SyncConfig config) => Resolve(config) is not null;

    /// <summary>True when the user overrides the built-in registration.</summary>
    public static bool UsesCustomId(SyncConfig config) => IsUsable(config.ClientId?.Trim());
}
