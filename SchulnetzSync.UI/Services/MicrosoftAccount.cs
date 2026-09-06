using SchulnetzSync.Core.Configuration;

namespace SchulnetzSync.UI.Services;

/// <summary>
/// Resolves which Entra application ID to sign in with.
///
/// Normalfall: Die App bringt ihre eigene Registrierung mit
/// (<see cref="AppConstants.ClientId"/>) — der Benutzer meldet sich nur an und
/// sieht nie ein Azure-Portal. Das Feld in den Einstellungen ist nur für
/// Fortgeschrittene, die eine eigene Registrierung verwenden wollen.
/// </summary>
public static class MicrosoftAccount
{
    /// <summary>True when the string looks like a real GUID and not a placeholder.</summary>
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
    /// The application ID to use: a user-supplied one wins over the built-in one.
    /// Null when neither is usable — then Outlook sync is unavailable.
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
