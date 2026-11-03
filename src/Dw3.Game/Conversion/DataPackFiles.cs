using Dw3.Data;

namespace Dw3Production.Conversion;

/// <summary>
/// Resolves pack-relative file paths for the runtime loaders. A path that escapes the active
/// data directory is a corrupt contract; an absent file is an unavailable resource.
/// </summary>
internal static class DataPackFiles
{
    /// <summary>Maps a pack-relative path to an absolute path and checks that the file exists, so loaders fail with a cache error before opening anything.</summary>
    /// <param name="pack">The active data pack, which rejects paths escaping its root.</param>
    /// <param name="relativePath">Path relative to the pack root, as stored in the SQLite tables.</param>
    /// <param name="context">Short description of the resource, used in the error message.</param>
    /// <returns>An absolute path to an existing file.</returns>
    /// <exception cref="CacheResourceException"><c>CACHE_INVALID</c> when the path is rejected, <c>CACHE_RESOURCE_UNAVAILABLE</c> when the file is absent.</exception>
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
