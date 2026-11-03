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
    /// <param name="arguments">Command-line arguments, as passed to the entry point.</param>
    /// <param name="output">Standard output stream for normal program output.</param>
    /// <param name="error">Standard error stream for diagnostics and usage messages.</param>
    /// <returns>POSIX exit code: 0 on success, 1 on extraction error, 2 on usage error.</returns>
    public static int Run(IReadOnlyList<string> arguments, TextWriter output, TextWriter error) =>
        Run(arguments, output, error, DiscSource.Open);

    /// <summary>
    /// Dispatches a CLI invocation against discs opened by <paramref name="openDisc"/>. The public
    /// overload passes <see cref="DiscSource.Open"/>, which reads the BIN named by a CUE sheet; tests
    /// pass a function that returns an in-memory disc built from fabricated files.
    /// </summary>
    /// <param name="arguments">Command-line arguments, as passed to the entry point.</param>
    /// <param name="output">Standard output stream for normal program output.</param>
    /// <param name="error">Standard error stream for diagnostics and usage messages.</param>
    /// <param name="openDisc">Maps the <c>--disc</c> value to an open disc; every caller disposes the handle it gets.</param>
    /// <returns>POSIX exit code: 0 on success, 1 on extraction error, 2 on usage error.</returns>
    internal static int Run(IReadOnlyList<string> arguments, TextWriter output, TextWriter error, Func<string, IDiscSource> openDisc)
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
            return RunListFiles(arguments, output, error, openDisc);
        }

        if (arguments.Count >= 1 && arguments[0] == "extract")
        {
            return RunExtract(arguments, output, error, openDisc);
        }

        if (arguments.Count >= 1 && arguments[0] == "analyze-dialogue")
        {
            return DialogueAnalysisCommand.Run(arguments, output, error, openDisc);
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

    /// <summary>
    /// Parses the extract subcommand arguments, validates required fields, and runs either the full
    /// extraction pipeline or a single-domain dispatch.
    /// </summary>
    /// <param name="arguments">Full argument list including the "extract" verb.</param>
    /// <param name="output">Receives JSONL progress events on full extraction or one completion line on single-domain.</param>
    /// <param name="error">Receives usage or extraction error messages.</param>
    /// <param name="openDisc">Maps the <c>--disc</c> value to an open disc; each step opens and disposes its own handle.</param>
    /// <returns>0 on success, 1 on extraction error, 2 on usage error.</returns>
    private static int RunExtract(IReadOnlyList<string> arguments, TextWriter output, TextWriter error, Func<string, IDiscSource> openDisc)
    {
        bool jsonlRequested = arguments.Skip(1).Any(
            arg => string.Equals(arg, "--progress", StringComparison.Ordinal));

        ExtractOptions? opts = TryParseExtractOptions(arguments, jsonlRequested, output, error);
        if (opts is null)
        {
            return 2;
        }

        if (!string.Equals(opts.ProgressFormat, "jsonl", StringComparison.Ordinal) && opts.ProgressFormat is not null)
        {
            return ExtractUsageError("Only --progress jsonl is supported.", jsonlRequested, output, error);
        }

        if (string.IsNullOrWhiteSpace(opts.CuePath) || string.IsNullOrWhiteSpace(opts.OutputPath))
        {
            return ExtractUsageError("Both --disc and --out are required.", jsonlRequested, output, error);
        }

        if (opts.OnlyDomain is null)
        {
            if (!string.Equals(opts.ProgressFormat, "jsonl", StringComparison.Ordinal))
            {
                return ExtractUsageError("Full extraction requires --progress jsonl.", false, output, error);
            }

            return FullExtractionStep.Extract(() => openDisc(opts.CuePath), opts.OutputPath, output);
        }

        if (opts.ProgressFormat is not null)
        {
            return ExtractUsageError("--progress jsonl is only available for full extraction.", true, output, error);
        }

        return DispatchSingleDomain(opts.OnlyDomain, () => openDisc(opts.CuePath), opts.OutputPath, output, error);
    }

    /// <summary>
    /// Parses <c>--disc</c>, <c>--out</c>, <c>--only</c>, and <c>--progress</c> from the extract
    /// argument list, writing a usage error and returning null on the first unrecognised token.
    /// </summary>
    /// <param name="arguments">Full argument list including the "extract" verb.</param>
    /// <param name="jsonlRequested">Whether JSONL progress output was requested (drives error format).</param>
    /// <param name="output">Used when writing JSONL-format errors.</param>
    /// <param name="error">Used when writing plain-text errors.</param>
    /// <returns>The parsed options, or <see langword="null"/> on a usage error.</returns>
    private static ExtractOptions? TryParseExtractOptions(
        IReadOnlyList<string> arguments,
        bool jsonlRequested,
        TextWriter output,
        TextWriter error)
    {
        string? cuePath = null;
        string? outputPath = null;
        string? onlyDomain = null;
        string? progressFormat = null;
        for (int index = 1; index < arguments.Count; index++)
        {
            string option = arguments[index];
            if (option is "--disc" or "--out" or "--only" or "--progress")
            {
                if (++index >= arguments.Count)
                {
                    ExtractUsageError($"Missing value for {option}.", jsonlRequested, output, error);
                    return null;
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
                return null;
            }
        }

        return new ExtractOptions(cuePath, outputPath, onlyDomain, progressFormat);
    }

    /// <summary>
    /// Routes a single domain name to the appropriate extraction step.
    /// Tries field domains first, then content domains, then battle domains.
    /// </summary>
    /// <param name="domain">The domain name (e.g. <c>"zones"</c>, <c>"battle"</c>).</param>
    /// <param name="openDisc">Opens the disc for the chosen step; the step disposes the handle before returning.</param>
    /// <param name="outputPath">Root of the data pack to write into.</param>
    /// <param name="output">Receives one JSON event line on success.</param>
    /// <param name="error">Receives one plain-text error line on failure.</param>
    /// <returns>0 on success, 1 on extraction error, 2 on unknown domain.</returns>
    private static int DispatchSingleDomain(
        string domain,
        Func<IDiscSource> openDisc,
        string outputPath,
        TextWriter output,
        TextWriter error)
    {
        int? result = TryDispatchFieldDomain(domain, openDisc, outputPath, output, error);
        result ??= TryDispatchContentDomain(domain, openDisc, outputPath, output, error);
        result ??= TryDispatchBattleDomain(domain, openDisc, outputPath, output, error);
        if (result is null)
        {
            error.WriteLine($"Unsupported extraction domain: {domain}");
            return 2;
        }

        return result.Value;
    }

    /// <summary>
    /// Attempts to dispatch a field-geometry domain: zones, collision, player, water, climb, drop, npc.
    /// Returns <see langword="null"/> for unrecognised names.
    /// </summary>
    /// <param name="domain">The domain name to match.</param>
    /// <param name="openDisc">Opens the disc for a field step; forwarded unchanged to the step.</param>
    /// <param name="outputPath">Root of the data pack to write into.</param>
    /// <param name="output">Receives one JSON event line on success.</param>
    /// <param name="error">Receives one plain-text error line on failure.</param>
    /// <returns>The extraction exit code, or <see langword="null"/> when the domain is not a field domain.</returns>
    private static int? TryDispatchFieldDomain(
        string domain,
        Func<IDiscSource> openDisc,
        string outputPath,
        TextWriter output,
        TextWriter error)
    {
        if (string.Equals(domain, "zones", StringComparison.Ordinal))
        {
            return ZoneExtractionStep.Extract(openDisc, outputPath, domain, output, error);
        }

        if (string.Equals(domain, "collision", StringComparison.Ordinal))
        {
            return CollisionExtractionStep.Extract(openDisc, outputPath, domain, output, error);
        }

        if (string.Equals(domain, "player", StringComparison.Ordinal))
        {
            return PlayerExtractionStep.Extract(openDisc, outputPath, domain, output, error);
        }

        if (string.Equals(domain, "water", StringComparison.Ordinal))
        {
            return WaterExtractionStep.Extract(openDisc, outputPath, output, error);
        }

        if (string.Equals(domain, "climb", StringComparison.Ordinal) ||
            string.Equals(domain, "drop", StringComparison.Ordinal))
        {
            return ActionExtractionStep.Extract(openDisc, outputPath, domain, output, error);
        }

        if (string.Equals(domain, "npc", StringComparison.Ordinal))
        {
            return NpcExtractionStep.Extract(openDisc, outputPath, output, error);
        }

        return null;
    }

    /// <summary>
    /// Attempts to dispatch a content domain: cue, icon, font, text, dialogue, talk-box.
    /// Returns <see langword="null"/> for unrecognised names.
    /// </summary>
    /// <param name="domain">The domain name to match.</param>
    /// <param name="openDisc">Opens the disc for a content step; forwarded unchanged to the step.</param>
    /// <param name="outputPath">Root of the data pack to write into.</param>
    /// <param name="output">Receives one JSON event line on success.</param>
    /// <param name="error">Receives one plain-text error line on failure.</param>
    /// <returns>The extraction exit code, or <see langword="null"/> when the domain is not a content domain.</returns>
    private static int? TryDispatchContentDomain(
        string domain,
        Func<IDiscSource> openDisc,
        string outputPath,
        TextWriter output,
        TextWriter error)
    {
        if (string.Equals(domain, "cue", StringComparison.Ordinal) ||
            string.Equals(domain, "icon", StringComparison.Ordinal))
        {
            return FieldcomExtractionStep.Extract(openDisc, outputPath, domain, output, error);
        }

        if (string.Equals(domain, "font", StringComparison.Ordinal) ||
            string.Equals(domain, "text", StringComparison.Ordinal))
        {
            return TextExtractionStep.Extract(openDisc, outputPath, domain, output, error);
        }

        if (string.Equals(domain, "dialogue", StringComparison.Ordinal))
        {
            return DialogueExtractionStep.Extract(openDisc, outputPath, output, error);
        }

        if (string.Equals(domain, "talk-box", StringComparison.Ordinal))
        {
            return TalkBoxExtractionStep.Extract(openDisc, outputPath, output, error);
        }

        return null;
    }

    /// <summary>
    /// Attempts to dispatch a battle domain: encounter, battle, digivolution, battle-models,
    /// battle-stages, battle-hud, battle-report. Returns <see langword="null"/> for unrecognised names.
    /// </summary>
    /// <param name="domain">The domain name to match.</param>
    /// <param name="openDisc">Opens the disc for a battle step; forwarded unchanged to the step.</param>
    /// <param name="outputPath">Root of the data pack to write into.</param>
    /// <param name="output">Receives one JSON event line on success.</param>
    /// <param name="error">Receives one plain-text error line on failure.</param>
    /// <returns>The extraction exit code, or <see langword="null"/> when the domain is not a battle domain.</returns>
    private static int? TryDispatchBattleDomain(
        string domain,
        Func<IDiscSource> openDisc,
        string outputPath,
        TextWriter output,
        TextWriter error)
    {
        if (string.Equals(domain, "encounter", StringComparison.Ordinal))
        {
            return EncounterExtractionStep.Extract(openDisc, outputPath, output, error);
        }

        if (string.Equals(domain, "battle", StringComparison.Ordinal) ||
            string.Equals(domain, "digivolution", StringComparison.Ordinal))
        {
            return BattleExtractionStep.Extract(openDisc, outputPath, domain, output, error);
        }

        if (string.Equals(domain, "battle-models", StringComparison.Ordinal))
        {
            return BattleModelExtractionStep.Extract(openDisc, outputPath, output, error);
        }

        if (string.Equals(domain, "battle-stages", StringComparison.Ordinal))
        {
            return BattleStageExtractionStep.Extract(openDisc, outputPath, output, error);
        }

        if (string.Equals(domain, "battle-hud", StringComparison.Ordinal))
        {
            return BattleHudExtractionStep.Extract(openDisc, outputPath, output, error);
        }

        if (string.Equals(domain, "battle-report", StringComparison.Ordinal))
        {
            return BattleReportExtractionStep.Extract(openDisc, outputPath, output, error);
        }

        return null;
    }

    /// <summary>
    /// Writes a usage error in either JSONL format (when <paramref name="jsonlRequested"/> is true) or
    /// plain text, then returns exit code 2.
    /// </summary>
    /// <param name="message">The usage error message to emit.</param>
    /// <param name="jsonlRequested">When true, writes a JSONL error object to <paramref name="output"/>; otherwise
    /// writes plain text to <paramref name="error"/>.</param>
    /// <param name="output">Used for JSONL-format errors.</param>
    /// <param name="error">Used for plain-text errors.</param>
    /// <returns>Always 2 (POSIX usage error).</returns>
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
    /// Output format (one line per file): &lt;sha256&gt;  &lt;iso/path&gt;.
    /// </summary>
    /// <param name="arguments">Full argument list including the "list-files" verb.</param>
    /// <param name="output">Receives one line per disc file: sha256  iso/path.</param>
    /// <param name="error">Receives the usage message when --disc is absent.</param>
    /// <param name="openDisc">Maps the <c>--disc</c> value to an open disc; the handle is disposed before returning.</param>
    /// <returns>0 on success, 1 on disc error, 2 on missing --disc argument.</returns>
    private static int RunListFiles(IReadOnlyList<string> arguments, TextWriter output, TextWriter error, Func<string, IDiscSource> openDisc)
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
            using IDiscSource disc = openDisc(cuePath);
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

    /// <summary>Groups the parsed options for the extract subcommand.</summary>
    private sealed record ExtractOptions(
        string? CuePath,
        string? OutputPath,
        string? OnlyDomain,
        string? ProgressFormat);
}
