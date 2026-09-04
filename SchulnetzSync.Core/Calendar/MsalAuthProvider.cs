using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;

namespace SchulnetzSync.Core.Calendar;

/// <summary>
/// MSAL-based authentication provider for Microsoft Graph.
/// Token cache is persisted under %LOCALAPPDATA%\Semestria via DPAPI.
/// </summary>
public sealed class MsalAuthProvider
{
    private const string Authority = "https://login.microsoftonline.com/common";
    private static readonly string[] Scopes = ["Calendars.ReadWrite"];

    private readonly IPublicClientApplication _app;

    public MsalAuthProvider(string clientId)
    {
        _app = PublicClientApplicationBuilder
            .Create(clientId)
            .WithAuthority(Authority)
            .WithDefaultRedirectUri()   // uses http://localhost for desktop apps
            .Build();

        RegisterTokenCache(_app.UserTokenCache);
    }

    // -----------------------------------------------------------------------
    // Public methods
    // -----------------------------------------------------------------------

    /// <summary>
    /// Attempts a silent token acquisition using the cached credentials.
    /// Throws <see cref="InteractiveLoginRequiredException"/> when no valid
    /// token exists — the caller must then decide whether to open a browser.
    /// </summary>
    public async Task<string> AcquireTokenSilentAsync(CancellationToken ct = default)
    {
        var accounts = await _app.GetAccountsAsync();
        var account  = accounts.FirstOrDefault();

        try
        {
            var result = await _app
                .AcquireTokenSilent(Scopes, account)
                .ExecuteAsync(ct);
            return result.AccessToken;
        }
        catch (MsalUiRequiredException)
        {
            throw new InteractiveLoginRequiredException();
        }
    }

    /// <summary>
    /// Opens the system browser for interactive sign-in.
    /// Returns the access token on success.
    /// </summary>
    public async Task<string> AcquireTokenInteractiveAsync(CancellationToken ct = default)
    {
        var result = await _app
            .AcquireTokenInteractive(Scopes)
            .ExecuteAsync(ct);
        return result.AccessToken;
    }

    /// <summary>
    /// Signs out all cached accounts and clears the token cache.
    /// </summary>
    public async Task SignOutAsync(CancellationToken ct = default)
    {
        var accounts = (await _app.GetAccountsAsync()).ToList();
        foreach (var account in accounts)
            await _app.RemoveAsync(account);
    }

    /// <summary>True when at least one account is cached (user is signed in).</summary>
    public async Task<bool> IsSignedInAsync()
        => (await _app.GetAccountsAsync()).Any();

    // -----------------------------------------------------------------------
    // Token cache persistence
    // -----------------------------------------------------------------------

    private static void RegisterTokenCache(ITokenCache tokenCache)
    {
        var cacheDir  = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Semestria");

        Directory.CreateDirectory(cacheDir);

        var storageProps = new StorageCreationPropertiesBuilder(
                "token_cache.bin", cacheDir)
            .WithUnprotectedFile()   // DPAPI protection added below
            .Build();

        // MsalCacheHelper handles DPAPI on Windows automatically.
        var helper = MsalCacheHelper.CreateAsync(storageProps)
            .GetAwaiter().GetResult();

        helper.RegisterCache(tokenCache);
    }
}
