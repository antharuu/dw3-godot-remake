using System.Text.RegularExpressions;

namespace Dw3.Disc;

/// <summary>
/// Parses a CUE sheet and resolves the referenced BIN file path.
/// Supports only the single-track BIN/CUE format used by the supported disc.
/// </summary>
internal static class CueParser
{
    // Matches: FILE "quoted name" BINARY  or  FILE bare_name BINARY
    private static readonly Regex FileDirective = new(
        @"^\s*FILE\s+(?:""(?<quoted>[^""]+)""|(?<bare>\S+))\s+(?<kind>\S+)\s*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Parses <paramref name="cuePath"/> and returns the absolute path to the referenced BIN file.
    /// </summary>
    /// <exception cref="DiscSourceException">
    /// <see cref="DiscSourceException.FormatInvalid"/> on any structural problem with the CUE or BIN.
    /// </exception>
    public static string ResolveBinPath(string cuePath)
    {
        string cueText;
        try
        {
            cueText = File.ReadAllText(cuePath, System.Text.Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new DiscSourceException(
                DiscSourceException.FormatInvalid,
                $"CUE sheet cannot be read: {cuePath}", ex);
        }

        // Collect all FILE directives; skip comments and blank lines.
        var references = new List<(string FileName, string Kind)>();
        foreach (string line in cueText.Split('\n'))
        {
            string stripped = line.Trim();
            if (stripped.Length == 0 || stripped.StartsWith("REM ", StringComparison.OrdinalIgnoreCase))
                continue;
            if (!stripped.StartsWith("FILE", StringComparison.OrdinalIgnoreCase))
                continue;
            Match m = FileDirective.Match(line);
            if (!m.Success)
                throw new DiscSourceException(DiscSourceException.FormatInvalid,
                    $"Malformed FILE directive in CUE: {cuePath}");
            string fileName = m.Groups["quoted"].Success ? m.Groups["quoted"].Value : m.Groups["bare"].Value;
            references.Add((fileName, m.Groups["kind"].Value));
        }

        // Exactly one FILE directive required.
        if (references.Count != 1)
            throw new DiscSourceException(DiscSourceException.FormatInvalid,
                $"CUE must reference exactly one BIN file; found {references.Count} FILE directive(s): {cuePath}");

        var (targetName, targetKind) = references[0];

        if (!targetKind.Equals("BINARY", StringComparison.OrdinalIgnoreCase))
            throw new DiscSourceException(DiscSourceException.FormatInvalid,
                $"CUE FILE type must be BINARY, not '{targetKind}': {cuePath}");

        // Reject absolute paths and directory traversal.
        string normalised = targetName.Replace('\\', '/');
        if (Path.IsPathRooted(normalised) || normalised.Contains(".."))
            throw new DiscSourceException(DiscSourceException.FormatInvalid,
                $"CUE BIN path must be a relative local filename: {targetName}");

        if (!normalised.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
            throw new DiscSourceException(DiscSourceException.FormatInvalid,
                $"CUE must reference a .bin file, not '{targetName}'");

        // Resolve relative to the CUE directory.
        string cueDir = Path.GetDirectoryName(Path.GetFullPath(cuePath))
            ?? throw new DiscSourceException(DiscSourceException.FormatInvalid,
                $"Cannot determine directory of CUE: {cuePath}");
        string[] parts = normalised.Split('/');
        string binPath = Path.GetFullPath(Path.Combine(cueDir, Path.Combine(parts)));

        if (!File.Exists(binPath))
            throw new DiscSourceException(DiscSourceException.FormatInvalid,
                $"BIN file referenced by CUE not found: {binPath}");

        return binPath;
    }
}
