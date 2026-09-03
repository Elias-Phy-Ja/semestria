namespace SchulnetzSync.Core.Calendar;

/// <summary>
/// Thrown by <see cref="MsalAuthProvider.AcquireTokenSilentAsync"/> when the
/// cached token has expired and an interactive browser login is required.
///
/// In --silent mode the caller must catch this, show a toast, and exit with
/// code 3 instead of opening a browser window.
/// </summary>
public sealed class InteractiveLoginRequiredException : Exception
{
    public InteractiveLoginRequiredException()
        : base("Das Zugriffstoken ist abgelaufen. Bitte starte die App manuell und melde dich an.") { }
}
