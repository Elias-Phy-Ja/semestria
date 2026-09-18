using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;

namespace SchulnetzSync.Core.Calendar;

/// <summary>
/// Sign-in against Microsoft Graph through MSAL. The token cache lives under
/// %LOCALAPPDATA%\Semestria and is protected by DPAPI.
/// </summary>
public sealed class MsalAuthProvider
{
    // Has to match the "signInAudience" of the app registration:
    //   PersonalMicrosoftAccount           -> /consumers
    //   AzureADandPersonalMicrosoftAccount -> /common
    // The registration we ship is PersonalMicrosoftAccount, hence /consumers.
    // With /common Microsoft rejects the request outright (userAudience).
    private const string Authority = "https://login.microsoftonline.com/consumers";
    private static readonly string[] Scopes = ["Calendars.ReadWrite"];

    private readonly IPublicClientApplication _app;

    public MsalAuthProvider(string clientId)
    {
        _app = PublicClientApplicationBuilder
            .Create(clientId)
            .WithAuthority(Authority)
            .WithDefaultRedirectUri()   // http://localhost, the desktop default
            .Build();

        RegisterTokenCache(_app.UserTokenCache);
    }

    /// <summary>
    /// Tries to get a token from the cache alone. Throws
    /// <see cref="InteractiveLoginRequiredException"/> when that is not possible, so
    /// the caller can decide whether opening a browser is appropriate right now.
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

    /// <summary>Opens the system browser for sign-in and returns the token.</summary>
    public async Task<string> AcquireTokenInteractiveAsync(CancellationToken ct = default)
    {
        var result = await _app
            .AcquireTokenInteractive(Scopes)
            .ExecuteAsync(ct);
        return result.AccessToken;
    }

    /// <summary>Removes every cached account, which empties the token cache.</summary>
    public async Task SignOutAsync(CancellationToken ct = default)
    {
        var accounts = (await _app.GetAccountsAsync()).ToList();
        foreach (var account in accounts)
            await _app.RemoveAsync(account);
    }

    /// <summary>True when an account is cached, i.e. the user is signed in.</summary>
    public async Task<bool> IsSignedInAsync()
        => (await _app.GetAccountsAsync()).Any();

    // ── Token cache ─────────────────────────────────────────────────────────

    private static void RegisterTokenCache(ITokenCache tokenCache)
    {
        var cacheDir  = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Semestria");

        Directory.CreateDirectory(cacheDir);

        var storageProps = new StorageCreationPropertiesBuilder(
                "token_cache.bin", cacheDir)
            .WithUnprotectedFile()   // protection is added by the cache helper below
            .Build();

        // MsalCacheHelper wires up DPAPI on Windows by itself. Blocking here is fine:
        // it happens once while the provider is being constructed.
        var helper = MsalCacheHelper.CreateAsync(storageProps)
            .GetAwaiter().GetResult();

        helper.RegisterCache(tokenCache);
    }
}
