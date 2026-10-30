namespace SchulnetzSync.Core.Storage;

/// <summary>A stored data set exists but cannot be read.</summary>
public sealed class CorruptUserDataException(string fileName, Exception inner)
    : Exception($"{fileName} ist beschädigt.", inner)
{
    /// <summary>Name of the file, without its folder.</summary>
    public string FileName { get; } = fileName;
}
