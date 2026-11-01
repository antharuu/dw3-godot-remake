using Dw3.Data;

namespace Dw3Production.Conversion;

/// <summary>
/// Resolves pack-relative file paths for the runtime loaders. A path that escapes the active
/// data directory is a corrupt contract; an absent file is an unavailable resource.
/// </summary>
internal static class DataPackFiles
{
    /// <exception cref="CacheResourceException">The path is invalid or the file is absent.</exception>
    internal static string Require(DataPackReader pack, string relativePath, string context)
    {
        string path;
        try
        {
            path = pack.ResolvePath(relativePath);
        }
        catch (ArgumentException error)
        {
            throw new CacheResourceException("CACHE_INVALID", $"{context} has an invalid path: {error.Message}");
        }
        return File.Exists(path)
            ? path
            : throw new CacheResourceException("CACHE_RESOURCE_UNAVAILABLE", $"{context} missing at {path}");
    }
}
