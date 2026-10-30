using System.Security.Cryptography;

namespace SchulnetzSync.Core.Security;

/// <summary>
/// Windows DPAPI, tied to the signed-in Windows user. Text form is Base-64, the format
/// every stored feed URL already has.
/// </summary>
public sealed class DpapiSecretStore : ISecretStore
{
    public string Protect(byte[] secret)
        => Convert.ToBase64String(ProtectedData.Protect(secret, null, DataProtectionScope.CurrentUser));

    public byte[] Unprotect(string protectedSecret)
        => ProtectedData.Unprotect(Convert.FromBase64String(protectedSecret), null, DataProtectionScope.CurrentUser);
}
