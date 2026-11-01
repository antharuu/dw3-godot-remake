using System.Security.Cryptography;
using Dw3.Disc;

namespace Dw3.Extractor;

/// <summary>
/// Entry point for all CLI subcommands. Routes --version, list-files, extract,
/// and analyze-dialogue to their respective handlers.
/// </summary>
public static class Cli
{
    /// <summary>Dispatches a CLI invocation and returns a POSIX exit code.</summary>
    public static int Run(IReadOnlyList<string> arguments, TextWriter output, TextWriter error)
    {
        if (arguments.Count == 1 && arguments[0] == "--version")
        {
            Version version = typeof(Cli).Assembly.GetName().Version
                ?? throw new InvalidOperationException("Extractor assembly version is unavailable.");
            output.WriteLine(version.ToString(3));
            return 0;
        }

        if (arguments.Count >= 1 && arguments[0] == "list-files")
        {
            return RunListFiles(arguments, output, error);
        }

        if (arguments.Count >= 1 && arguments[0] == "extract")
        {
            return RunExtract(arguments, output, error);
        }

        if (arguments.Count >= 1 && arguments[0] == "analyze-dialogue")
        {
            return DialogueAnalysisCommand.Run(arguments, output, error);
        }

        string option = arguments.Count == 0 ? "<none>" : arguments[0];
        error.WriteLine($"Unknown option: {option}");
        error.WriteLine("Usage:");
        error.WriteLine("  Dw3.Extractor --version");
        error.WriteLine("  Dw3.Extractor list-files --disc <path/to/disc.cue>");
        error.WriteLine("  Dw3.Extractor analyze-dialogue --disc <path/to/disc.cue> [--details]");
        error.WriteLine("  Dw3.Extractor extract --disc <path/to/disc.cue> --out <data-root> --progress jsonl");
        error.WriteLine("  Dw3.Extractor extract --disc <path/to/disc.cue> --out <data-root> --only <domain>");
        error.WriteLine("  Full extraction atomically replaces <data-root>/data after every domain succeeds.");
        return 2;
    }

    private static int RunExtract(IReadOnlyList<string> arguments, TextWriter output, TextWriter error)
    {
        bool jsonlRequested = arguments.Skip(1).Any(
            arg => string.Equals(arg, "--progress", StringComparison.Ordinal));

        if (!TryParseExtractOptions(
            arguments, jsonlRequested, output, error,
            out string? cuePath, out string? outputPath, out string? onlyDomain, out string? progressFormat))
        {
            return 2;
        }

        if (!string.Equals(progressFormat, "jsonl", StringComparison.Ordinal) && progressFormat is not null)
        {
            return ExtractUsageError("Only --progress jsonl is supported.", jsonlRequested, output, error);
        }

        if (string.IsNullOrWhiteSpace(cuePath) || string.IsNullOrWhiteSpace(outputPath))
        {
            return ExtractUsageError("Both --disc and --out are required.", jsonlRequested, output, error);
        }

        if (onlyDomain is null)
        {
            if (!string.Equals(progressFormat, "jsonl", StringComparison.Ordinal))
            {
                return ExtractUsageError("Full extraction requires --progress jsonl.", false, output, error);
            }

            return FullExtractionStep.Extract(cuePath, outputPath, output);
        }

        if (progressFormat is not null)
        {
            return ExtractUsageError("--progress jsonl is only available for full extraction.", true, output, error);
        }

        return DispatchSingleDomain(onlyDomain, cuePath, outputPath, output, error);
    }

    private static bool TryParseExtractOptions(
        IReadOnlyList<string> arguments,
        bool jsonlRequested,
        TextWriter output,
        TextWriter error,
        out string? cuePath,
        out string? outputPath,
        out string? onlyDomain,
        out string? progressFormat)
    {
        cuePath = null;
        outputPath = null;
        onlyDomain = null;
        progressFormat = null;
        for (int index = 1; index < arguments.Count; index++)
        {
            string option = arguments[index];
            if (option is "--disc" or "--out" or "--only" or "--progress")
            {
                if (++index >= arguments.Count)
                {
                    ExtractUsageError($"Missing value for {option}.", jsonlRequested, output, error);
                    return false;
                }

                switch (option)
                {
                    case "--disc": cuePath = arguments[index]; break;
                    case "--out": outputPath = arguments[index]; break;
                    case "--only": onlyDomain = arguments[index]; break;
                    case "--progress": progressFormat = arguments[index]; break;
                }
            }
            else
            {
                ExtractUsageError($"Unknown extract option: {option}", jsonlRequested, output, error);
                return false;
            }
        }

        return true;
    }

    private static int DispatchSingleDomain(
        string domain,
        string cuePath,
        string outputPath,
        TextWriter output,
        TextWriter error)
    {
        int? result = TryDispatchFieldDomain(domain, cuePath, outputPath, output, error);
        result ??= TryDispatchContentDomain(domain, cuePath, outputPath, output, error);
        result ??= TryDispatchBattleDomain(domain, cuePath, outputPath, output, error);
        if (result is null)
        {
            error.WriteLine($"Unsupported extraction domain: {domain}");
            return 2;
        }

        return result.Value;
    }

    private static int? TryDispatchFieldDomain(
        string domain,
        string cuePath,
        string outputPath,
        TextWriter output,
        TextWriter error)
    {
        if (string.Equals(domain, "zones", StringComparison.Ordinal))
        {
            return ZoneExtractionStep.Extract(cuePath, outputPath, domain, output, error);
        }

        if (string.Equals(domain, "collision", StringComparison.Ordinal))
        {
            return CollisionExtractionStep.Extract(cuePath, outputPath, domain, output, error);
        }

        if (string.Equals(domain, "player", StringComparison.Ordinal))
        {
            return PlayerExtractionStep.Extract(cuePath, outputPath, domain, output, error);
        }

        if (string.Equals(domain, "water", StringComparison.Ordinal))
        {
            return WaterExtractionStep.Extract(cuePath, outputPath, output, error);
        }

        if (string.Equals(domain, "climb", StringComparison.Ordinal) ||
            string.Equals(domain, "drop", StringComparison.Ordinal))
        {
            return ActionExtractionStep.Extract(cuePath, outputPath, domain, output, error);
        }

        if (string.Equals(domain, "npc", StringComparison.Ordinal))
        {
            return NpcExtractionStep.Extract(cuePath, outputPath, output, error);
        }

        return null;
    }

    private static int? TryDispatchContentDomain(
        string domain,
        string cuePath,
        string outputPath,
        TextWriter output,
        TextWriter error)
    {
        if (string.Equals(domain, "cue", StringComparison.Ordinal) ||
            string.Equals(domain, "icon", StringComparison.Ordinal))
        {
            return FieldcomExtractionStep.Extract(cuePath, outputPath, domain, output, error);
        }

        if (string.Equals(domain, "font", StringComparison.Ordinal) ||
            string.Equals(domain, "text", StringComparison.Ordinal))
        {
            return TextExtractionStep.Extract(cuePath, outputPath, domain, output, error);
        }

        if (string.Equals(domain, "dialogue", StringComparison.Ordinal))
        {
            return DialogueExtractionStep.Extract(cuePath, outputPath, output, error);
        }

        if (string.Equals(domain, "talk-box", StringComparison.Ordinal))
        {
            return TalkBoxExtractionStep.Extract(cuePath, outputPath, output, error);
        }

        return null;
    }

    private static int? TryDispatchBattleDomain(
        string domain,
        string cuePath,
        string outputPath,
        TextWriter output,
        TextWriter error)
    {
        if (string.Equals(domain, "encounter", StringComparison.Ordinal))
        {
            return EncounterExtractionStep.Extract(cuePath, outputPath, output, error);
        }

        if (string.Equals(domain, "battle", StringComparison.Ordinal) ||
            string.Equals(domain, "digivolution", StringComparison.Ordinal))
        {
            return BattleExtractionStep.Extract(cuePath, outputPath, domain, output, error);
        }

        if (string.Equals(domain, "battle-models", StringComparison.Ordinal))
        {
            return BattleModelExtractionStep.Extract(cuePath, outputPath, output, error);
        }

        if (string.Equals(domain, "battle-stages", StringComparison.Ordinal))
        {
            return BattleStageExtractionStep.Extract(cuePath, outputPath, output, error);
        }

        if (string.Equals(domain, "battle-hud", StringComparison.Ordinal))
        {
            return BattleHudExtractionStep.Extract(cuePath, outputPath, output, error);
        }

        if (string.Equals(domain, "battle-report", StringComparison.Ordinal))
        {
            return BattleReportExtractionStep.Extract(cuePath, outputPath, output, error);
        }

        return null;
    }

    private static int ExtractUsageError(string message, bool jsonlRequested, TextWriter output, TextWriter error)
    {
        if (jsonlRequested)
        {
            FullExtractionStep.WriteError(output, "USAGE", message);
        }
        else
        {
            error.WriteLine(message);
            error.WriteLine("Usage: Dw3.Extractor extract --disc <path/to/disc.cue> --out <data-root> [--only zones|collision|player|water|climb|drop|cue|icon|npc|text|font|dialogue|talk-box|encounter|battle|digivolution|battle-models|battle-stages|battle-hud|battle-report]");
        }

        return 2;
    }

    // ── list-files subcommand ──────────────────────────────────────────────

    /// <summary>
    /// Lists every file on the disc with its SHA-256 digest, sorted by ISO path.
    /// Output format (one line per file): &lt;sha256&gt;  &lt;iso/path&gt;
    /// </summary>
    private static int RunListFiles(IReadOnlyList<string> arguments, TextWriter output, TextWriter error)
    {
        string? cuePath = null;
        for (int i = 1; i < arguments.Count - 1; i++)
        {
            if (arguments[i] == "--disc")
            {
                cuePath = arguments[i + 1];
                break;
            }
        }

        if (cuePath is null)
        {
            error.WriteLine("Usage: Dw3.Extractor list-files --disc <path/to/disc.cue>");
            return 2;
        }

        try
        {
            using var disc = DiscSource.Open(cuePath);
            var files = disc.ListFiles()
                            .OrderBy(f => f.IsoPath, StringComparer.Ordinal)
                            .ToList();

            foreach (var entry in files)
            {
                byte[] content = disc.ReadFile(entry);
                string sha256 = Convert.ToHexStringLower(SHA256.HashData(content));
                output.WriteLine($"{sha256}  {entry.IsoPath}");
            }

            return 0;
        }
        catch (DiscSourceException ex)
        {
            error.WriteLine($"Error [{ex.ErrorCode}]: {ex.Message}");
            return 1;
        }
    }
}
