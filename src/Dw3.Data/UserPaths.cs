namespace Dw3.Data;

/// <summary>
/// Resolves the per-user directory that owns extracted data packs.
/// </summary>
public static class UserPaths
{
    /// <summary>
    /// Gets the root directory for this application's extracted data, without creating it.
    /// </summary>
    /// <returns>An absolute platform-native data directory or the <c>DW3_DATA_DIR</c> override.</returns>
    public static string GetDataDirectory()
    {
        string? overrideDirectory = Environment.GetEnvironmentVariable("DW3_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(overrideDirectory))
        {
            return Path.GetFullPath(overrideDirectory);
        }

        if (OperatingSystem.IsWindows())
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "dw3-remake");
        }

        string homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (OperatingSystem.IsMacOS())
        {
            return Path.Combine(homeDirectory, "Library", "Application Support", "dw3-remake");
        }

        string? xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        string dataHome = string.IsNullOrWhiteSpace(xdgDataHome)
            ? Path.Combine(homeDirectory, ".local", "share")
            : xdgDataHome;
        return Path.GetFullPath(Path.Combine(dataHome, "dw3-remake"));
    }
}
