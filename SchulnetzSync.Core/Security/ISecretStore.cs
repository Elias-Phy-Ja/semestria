namespace SchulnetzSync.Core.Security;

/// <summary>
/// Protects secrets at rest, so that a copied file alone is useless. Holds the feed URL
/// today and the cloud keys later.
/// </summary>
public interface ISecretStore
{
    /// <summary>Returns the protected form of a secret as text, safe to write to a file.</summary>
    string Protect(byte[] secret);

    /// <summary>Returns the secret. Throws when it cannot be recovered on this machine.</summary>
    byte[] Unprotect(string protectedSecret);
}
