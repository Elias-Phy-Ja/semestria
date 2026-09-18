namespace SchulnetzSync.UI;

/// <summary>
/// Values that are needed all over the app and have to be changed in exactly one place.
/// </summary>
public static class AppConstants
{
    /// <summary>
    /// Application (client) ID of the app registration that ships with Semestria.
    /// Users sign in against this registration and never touch a Microsoft portal.
    ///
    /// A client id is not a secret. This is a public client using PKCE, so the id is meant
    /// to ship with the app; there is no client secret anywhere in this project.
    ///
    /// Registration "Semestria.61bc8af998c4", created on 3.9.2026 under the personal
    /// account. It lives in NO directory at all (a legacy consumer-context registration),
    /// which has consequences:
    ///
    ///   • Only personal Microsoft accounts can sign in (outlook.com, hotmail.com,
    ///     live.com, or any address registered as a private MS account).
    ///   • School and work accounts do NOT work.
    ///   • Microsoft has deprecated creating registrations like this one. Existing ones
    ///     keep working, new ones cannot be made this way any more.
    ///
    /// To support school accounts one day: create an own Entra tenant (needs an Azure
    /// account with a payment method on file, though Entra ID Free still costs nothing),
    /// register the app there with account type "accounts in any organizational directory
    /// and personal Microsoft accounts", and put the new id here. That is a new client id,
    /// so everyone has to sign in once more.
    ///
    /// Settings the registration needs:
    ///   • Authentication: platform "Mobile and desktop applications",
    ///     redirect http://localhost, public client enabled
    ///   • API permissions: Microsoft Graph, delegated, Calendars.ReadWrite
    /// </summary>
    public const string ClientId = "437f085a-2ec7-4dbd-aecd-46cd1468a268";

    public const string AppName    = "Semestria";
    /// <summary>
    /// The version shown to the user. Has to match Packaging\Package.appxmanifest,
    /// which spells it out with four parts: 2.2.0.0.
    /// </summary>
    public const string Version    = "2.2.0";

    /// <summary>Short form for the sidebar, e.g. "v2.2".</summary>
    public const string VersionShort = "v2.2";
    public const string Publisher  = "Elias Wyss";

    /// <summary>Version of the legal texts. Bumping it makes everyone accept them again.</summary>
    public const int LegalVersion = 1;

    /// <summary>Where feedback and bug reports go.</summary>
    public const string GitHubUrl = "https://github.com/Elias-Phy-Ja/semestria";

    /// <summary>Dashboard of the Synapkey web app, linked from the sidebar.</summary>
    public const string SynapkeyDashboardUrl = "https://synapkey.ch/dashboard.html";
}
