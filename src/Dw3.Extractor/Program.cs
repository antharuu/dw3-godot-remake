using System.Reflection;
using System.Security.Cryptography;
using Dw3.Disc;

namespace Dw3.Extractor;

public static class Cli
{
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
            return RunListFiles(arguments, output, error);
        if (arguments.Count >= 1 && arguments[0] == "extract")
            return RunExtract(arguments, output, error);
        if (arguments.Count >= 1 && arguments[0] == "analyze-dialogue")
            return DialogueAnalysisCommand.Run(arguments, output, error);

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
        string? cuePath = null;
        string? outputPath = null;
        string? onlyDomain = null;
        string? progressFormat = null;
        bool jsonlRequested = arguments.Skip(1).Any(argument => string.Equals(argument, "--progress", StringComparison.Ordinal));
        for (int index = 1; index < arguments.Count; index++)
        {
            string option = arguments[index];
            if (option is "--disc" or "--out" or "--only" or "--progress")
            {
                if (++index >= arguments.Count)
                    return ExtractUsageError($"Missing value for {option}.", jsonlRequested, output, error);
                switch (option)
                {
                    case "--disc": cuePath = arguments[index]; break;
                    case "--out": outputPath = arguments[index]; break;
                    case "--only": onlyDomain = arguments[index]; break;
                    case "--progress": progressFormat = arguments[index]; break;
                }
            }
            else
                return ExtractUsageError($"Unknown extract option: {option}", jsonlRequested, output, error);
        }
        if (!string.Equals(progressFormat, "jsonl", StringComparison.Ordinal) && progressFormat is not null)
            return ExtractUsageError("Only --progress jsonl is supported.", jsonlRequested, output, error);
        if (string.IsNullOrWhiteSpace(cuePath) || string.IsNullOrWhiteSpace(outputPath))
            return ExtractUsageError("Both --disc and --out are required.", jsonlRequested, output, error);
        if (onlyDomain is null)
        {
            if (!string.Equals(progressFormat, "jsonl", StringComparison.Ordinal))
                return ExtractUsageError("Full extraction requires --progress jsonl.", false, output, error);
            return FullExtractionStep.Extract(cuePath, outputPath, output);
        }
        if (progressFormat is not null)
            return ExtractUsageError("--progress jsonl is only available for full extraction.", true, output, error);
        if (string.Equals(onlyDomain, "zones", StringComparison.Ordinal))
            return ZoneExtractionStep.Extract(cuePath, outputPath, onlyDomain, output, error);
        if (string.Equals(onlyDomain, "collision", StringComparison.Ordinal))
            return CollisionExtractionStep.Extract(cuePath, outputPath, onlyDomain, output, error);
        if (string.Equals(onlyDomain, "player", StringComparison.Ordinal))
            return PlayerExtractionStep.Extract(cuePath, outputPath, onlyDomain, output, error);
        if (string.Equals(onlyDomain, "water", StringComparison.Ordinal))
            return WaterExtractionStep.Extract(cuePath, outputPath, output, error);
        if (string.Equals(onlyDomain, "climb", StringComparison.Ordinal) || string.Equals(onlyDomain, "drop", StringComparison.Ordinal))
            return ActionExtractionStep.Extract(cuePath, outputPath, onlyDomain, output, error);
        if (string.Equals(onlyDomain, "cue", StringComparison.Ordinal) || string.Equals(onlyDomain, "icon", StringComparison.Ordinal))
            return FieldcomExtractionStep.Extract(cuePath, outputPath, onlyDomain, output, error);
        if (string.Equals(onlyDomain, "npc", StringComparison.Ordinal))
            return NpcExtractionStep.Extract(cuePath, outputPath, output, error);
        if (string.Equals(onlyDomain, "font", StringComparison.Ordinal) || string.Equals(onlyDomain, "text", StringComparison.Ordinal))
            return TextExtractionStep.Extract(cuePath, outputPath, onlyDomain, output, error);
        if (string.Equals(onlyDomain, "dialogue", StringComparison.Ordinal))
            return DialogueExtractionStep.Extract(cuePath, outputPath, output, error);
        if (string.Equals(onlyDomain, "talk-box", StringComparison.Ordinal))
            return TalkBoxExtractionStep.Extract(cuePath, outputPath, output, error);
        if (string.Equals(onlyDomain, "encounter", StringComparison.Ordinal))
            return EncounterExtractionStep.Extract(cuePath, outputPath, output, error);
        if (string.Equals(onlyDomain, "battle", StringComparison.Ordinal) || string.Equals(onlyDomain, "digivolution", StringComparison.Ordinal))
            return BattleExtractionStep.Extract(cuePath, outputPath, onlyDomain, output, error);
        if (string.Equals(onlyDomain, "battle-models", StringComparison.Ordinal))
            return BattleModelExtractionStep.Extract(cuePath, outputPath, output, error);
        if (string.Equals(onlyDomain, "battle-stages", StringComparison.Ordinal))
            return BattleStageExtractionStep.Extract(cuePath, outputPath, output, error);
        if (string.Equals(onlyDomain, "battle-hud", StringComparison.Ordinal))
            return BattleHudExtractionStep.Extract(cuePath, outputPath, output, error);
        if (string.Equals(onlyDomain, "battle-report", StringComparison.Ordinal))
            return BattleReportExtractionStep.Extract(cuePath, outputPath, output, error);
        error.WriteLine($"Unsupported extraction domain: {onlyDomain}");
        return 2;
    }

    private static int ExtractUsageError(string message, bool jsonlRequested, TextWriter output, TextWriter error)
    {
        if (jsonlRequested)
            FullExtractionStep.WriteError(output, "USAGE", message);
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
                string sha256  = Convert.ToHexStringLower(SHA256.HashData(content));
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

public static class Program
{
    public static int Main(string[] arguments) => Cli.Run(arguments, Console.Out, Console.Error);
}
