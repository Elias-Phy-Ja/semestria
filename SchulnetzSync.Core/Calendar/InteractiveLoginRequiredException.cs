namespace SchulnetzSync.Core.Calendar;

/// <summary>
/// Thrown by <see cref="MsalAuthProvider.AcquireTokenSilentAsync"/> once the cached
/// token is gone and only a browser login can fix it.
///
/// In --silent mode the caller catches this, shows a toast and exits with code 3 —
/// a scheduled background run must never pop a login window at the user.
/// </summary>
public sealed class InteractiveLoginRequiredException : Exception
{
    public InteractiveLoginRequiredException()
        : base("Das Zugriffstoken ist abgelaufen. Bitte starte die App manuell und melde dich an.") { }
}
