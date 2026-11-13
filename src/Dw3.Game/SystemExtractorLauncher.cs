using System.Diagnostics;

namespace Dw3Production;

/// <summary>
/// Production extractor launcher: spawns the Dw3.Extractor binary as a child process.
/// The game process only passes the disc path as a command-line argument;
/// it never opens the disc files itself.
/// </summary>
public sealed class SystemExtractorLauncher : IExtractorLauncher
{
    private readonly string _extractorBinary;

    /// <summary>
    /// Initializes a new instance of the <see cref="SystemExtractorLauncher"/> class
    /// with the path to the Dw3.Extractor binary.
    /// </summary>
    /// <param name="extractorBinary">Absolute or relative path to the Dw3.Extractor executable.</param>
    public SystemExtractorLauncher(string extractorBinary)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(extractorBinary);
        _extractorBinary = extractorBinary;
    }

    /// <summary>
    /// Resolves the extractor binary path for the current environment.
    /// Prefers the <c>DW3_EXTRACTOR</c> environment variable (development override).
    /// Falls back to the debug build output path relative to <paramref name="repoRoot"/>.
    /// </summary>
    /// <param name="repoRoot">Root directory of the repository, used to construct the default debug output path.</param>
    /// <returns>Absolute or relative path to the Dw3.Extractor binary.</returns>
    public static string ResolveExtractorPath(string repoRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoRoot);
        string? env = System.Environment.GetEnvironmentVariable("DW3_EXTRACTOR");
        if (!string.IsNullOrWhiteSpace(env))
        {
            return env;
        }

        // Default: debug build produced by dotnet build Dw3.sln.
        return Path.Combine(repoRoot, "src", "Dw3.Extractor", "bin", "Debug", "net10.0", "Dw3.Extractor");
    }

    /// <inheritdoc/>
    public IExtractorProcess Launch(string discPath, string outputDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(discPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);
        EnsureSchemaMatches();

        var info = new ProcessStartInfo
        {
            FileName = _extractorBinary,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };

        // The disc path is passed as a command-line argument; the game process
        // never calls File.Open or any equivalent on it.
        info.ArgumentList.Add("extract");
        info.ArgumentList.Add("--disc");
        info.ArgumentList.Add(discPath);
        info.ArgumentList.Add("--out");
        info.ArgumentList.Add(outputDirectory);
        info.ArgumentList.Add("--progress");
        info.ArgumentList.Add("jsonl");

        Process process = Process.Start(info)
            ?? throw new InvalidOperationException("Failed to start the extractor process.");
        return new SystemExtractorProcess(process);
    }

    /// <summary>Rejects missing, outdated, or incompatible extractors before they touch the disc.</summary>
    private void EnsureSchemaMatches()
    {
        var info = new ProcessStartInfo
        {
            FileName = _extractorBinary,
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        info.ArgumentList.Add("--schema-version");
        using Process process = Process.Start(info)
            ?? throw new InvalidOperationException("Unable to check extractor schema.");
        Task<string> output = process.StandardOutput.ReadToEndAsync();
        if (!process.WaitForExit(10000))
        {
            process.Kill(entireProcessTree: true);
            throw new InvalidOperationException("Extractor schema check timed out. Rebuild the game and extractor.");
        }

        string expected = Dw3.Data.DataPackSchema.Version.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (process.ExitCode != 0 || !string.Equals(output.GetAwaiter().GetResult().Trim(), expected, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Extractor schema is incompatible with runtime schema {expected}. Rebuild the game and extractor together.");
        }
    }
}
