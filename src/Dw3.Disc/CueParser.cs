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
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        Regex.InfiniteMatchTimeout);

    /// <summary>
    /// Parses <paramref name="cuePath"/> and returns the absolute path to the referenced BIN file.
    /// </summary>
    /// <param name="cuePath">Absolute or relative path to the <c>.cue</c> file to parse.</param>
    /// <returns>The verified absolute path to the BIN file referenced by the CUE sheet.</returns>
    /// <exception cref="DiscSourceException">
    /// <see cref="DiscSourceException.FormatInvalid"/> on any structural problem with the CUE or BIN.
    /// </exception>
    public static string ResolveBinPath(string cuePath)
    {
        string cueText = ReadCueText(cuePath);
        var references = ExtractFileReferences(cueText, cuePath);
        ValidateSingleFileReference(references, cuePath);
        var (targetName, targetKind) = references[0];
        ValidateBinaryKind(targetKind, cuePath);
        string normalised = NormaliseBinName(targetName);
        return ResolveAbsolutePath(normalised, cuePath);
    }

    /// <summary>Reads the CUE sheet text, mapping I/O failures to a format error.</summary>
    /// <param name="cuePath">Path to the <c>.cue</c> file to read.</param>
    /// <returns>The raw text of the CUE sheet.</returns>
    private static string ReadCueText(string cuePath)
    {
        try
        {
            return File.ReadAllText(cuePath, System.Text.Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new DiscSourceException(
                DiscSourceException.FormatInvalid,
                $"CUE sheet cannot be read: {cuePath}",
                ex);
        }
    }

    /// <summary>
    /// Extracts all FILE directive references from the CUE text, skipping comments and blank lines.
    /// </summary>
    /// <param name="cueText">Raw text of the CUE sheet.</param>
    /// <param name="cuePath">Path used in error messages.</param>
    /// <returns>List of (FileName, Kind) tuples for each FILE directive found.</returns>
    private static List<(string FileName, string Kind)> ExtractFileReferences(string cueText, string cuePath)
    {
        var references = new List<(string FileName, string Kind)>();
        foreach (string line in cueText.Split('\n'))
        {
            CollectFileReference(line, cuePath, references);
        }

        return references;
    }

    /// <summary>
    /// Parses a single CUE line and appends a FILE directive entry to <paramref name="references"/>
    /// when the line contains a valid FILE directive.
    /// </summary>
    /// <param name="line">One line of the CUE sheet text.</param>
    /// <param name="cuePath">Path used in error messages.</param>
    /// <param name="references">Accumulator for FILE directive entries.</param>
    private static void CollectFileReference(string line, string cuePath, List<(string FileName, string Kind)> references)
    {
        string stripped = line.Trim();
        if (stripped.Length == 0 || stripped.StartsWith("REM ", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!stripped.StartsWith("FILE", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        Match m = FileDirective.Match(line);
        if (!m.Success)
        {
            throw new DiscSourceException(
                DiscSourceException.FormatInvalid,
                $"Malformed FILE directive in CUE: {cuePath}");
        }

        string fileName = m.Groups["quoted"].Success ? m.Groups["quoted"].Value : m.Groups["bare"].Value;
        references.Add((fileName, m.Groups["kind"].Value));
    }

    /// <summary>Throws when the CUE sheet does not reference exactly one BIN file.</summary>
    /// <param name="references">The collected FILE directive list.</param>
    /// <param name="cuePath">Path used in error messages.</param>
    private static void ValidateSingleFileReference(List<(string FileName, string Kind)> references, string cuePath)
    {
        if (references.Count != 1)
        {
            throw new DiscSourceException(
                DiscSourceException.FormatInvalid,
                $"CUE must reference exactly one BIN file; found {references.Count} FILE directive(s): {cuePath}");
        }
    }

    /// <summary>Throws when the FILE directive uses a type other than BINARY.</summary>
    /// <param name="targetKind">The type string from the FILE directive (e.g. BINARY, AUDIO).</param>
    /// <param name="cuePath">Path used in error messages.</param>
    private static void ValidateBinaryKind(string targetKind, string cuePath)
    {
        if (!targetKind.Equals("BINARY", StringComparison.OrdinalIgnoreCase))
        {
            throw new DiscSourceException(
                DiscSourceException.FormatInvalid,
                $"CUE FILE type must be BINARY, not '{targetKind}': {cuePath}");
        }
    }

    /// <summary>
    /// Normalises the BIN filename from the FILE directive, rejecting rooted paths,
    /// directory traversal, and non-.bin extensions.
    /// </summary>
    /// <param name="targetName">The raw file name from the FILE directive.</param>
    /// <returns>The normalised filename with forward slashes.</returns>
    private static string NormaliseBinName(string targetName)
    {
        // Reject absolute paths and directory traversal.
        string normalised = targetName.Replace('\\', '/');
        if (Path.IsPathRooted(normalised) || normalised.Contains(".."))
        {
            throw new DiscSourceException(
                DiscSourceException.FormatInvalid,
                $"CUE BIN path must be a relative local filename: {targetName}");
        }

        if (!normalised.EndsWith(".bin", StringComparison.OrdinalIgnoreCase))
        {
            throw new DiscSourceException(
                DiscSourceException.FormatInvalid,
                $"CUE must reference a .bin file, not '{targetName}'");
        }

        return normalised;
    }

    /// <summary>
    /// Resolves the normalised BIN filename to an absolute path relative to the CUE directory,
    /// verifying that the file exists.
    /// </summary>
    /// <param name="normalised">The forward-slash-separated relative BIN filename.</param>
    /// <param name="cuePath">Path to the CUE file, used to determine the directory.</param>
    /// <returns>The verified absolute path to the BIN file.</returns>
    private static string ResolveAbsolutePath(string normalised, string cuePath)
    {
        // Resolve relative to the CUE directory.
        string cueDir = Path.GetDirectoryName(Path.GetFullPath(cuePath))
            ?? throw new DiscSourceException(
                DiscSourceException.FormatInvalid,
                $"Cannot determine directory of CUE: {cuePath}");
        string[] parts = normalised.Split('/');
        string binPath = Path.GetFullPath(Path.Combine(cueDir, Path.Combine(parts)));

        if (!File.Exists(binPath))
        {
            throw new DiscSourceException(
                DiscSourceException.FormatInvalid,
                $"BIN file referenced by CUE not found: {binPath}");
        }

        return binPath;
    }
}
