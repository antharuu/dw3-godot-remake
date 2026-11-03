using System.Globalization;

namespace Dw3.QualityRatchet;

/// <summary>
/// Parses the command line of the ratchet.
/// </summary>
internal static class RatchetOptionsParser
{
    /// <summary>
    /// The help text printed with every usage error.
    /// </summary>
    public const string Usage = """
        Usage: dotnet run --project tools/Dw3.QualityRatchet -- <check|update|init> [options]
          check                  fail when a violation count goes up or a library is below the coverage minimum
          update                 write lower counts into the baseline, refuse anything else
          init                   create the baseline from the current measurement (only when none exists)
        Options:
          --repo <dir>           repository root (default: nearest parent folder holding Dw3.sln)
          --sarif-dir <dir>      SARIF logs (default: artifacts/quality/sarif)
          --coverage-dir <dir>   Cobertura reports (default: artifacts/quality/coverage)
          --merged-coverage <f>  merged Cobertura output (default: artifacts/quality/coverage.cobertura.xml)
          --baseline <file>      baseline (default: tools/quality-baseline.json)
          --require-coverage     fail when no Cobertura report is found
          --libraries <a,b>      init: libraries whose line coverage is checked
          --coverage-target <n>  init: minimum line coverage in percent (default: 80)
        """;

    /// <summary>
    /// The solution file that marks the repository root.
    /// </summary>
    private const string SolutionFileName = "Dw3.sln";

    /// <summary>
    /// The options that take a value.
    /// </summary>
    private static readonly HashSet<string> ValueOptions = new(StringComparer.Ordinal)
    {
        "--repo",
        "--sarif-dir",
        "--coverage-dir",
        "--merged-coverage",
        "--baseline",
        "--libraries",
        "--coverage-target",
    };

    /// <summary>
    /// Parses the arguments.
    /// </summary>
    /// <param name="arguments">The command followed by its options.</param>
    /// <param name="workingDirectory">The directory relative paths are resolved from.</param>
    /// <returns>The options.</returns>
    /// <exception cref="QualityInputException">When the command or an option is unknown or incomplete.</exception>
    public static RatchetOptions Parse(IReadOnlyList<string> arguments, string workingDirectory)
    {
        if (arguments.Count == 0)
        {
            throw new QualityInputException(Usage);
        }

        RatchetMode mode = ParseMode(arguments[0]);
        var pending = new Queue<string>(arguments.Skip(1));
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        bool requireCoverage = false;
        while (pending.TryDequeue(out string? name))
        {
            if (name == "--require-coverage")
            {
                requireCoverage = true;
            }
            else if (ValueOptions.Contains(name) && pending.TryDequeue(out string? value))
            {
                values[name] = value;
            }
            else
            {
                throw new QualityInputException($"Unknown or incomplete option '{name}'.\n{Usage}");
            }
        }

        return Build(mode, values, requireCoverage, workingDirectory);
    }

    /// <summary>
    /// Parses the command name. The --update spelling is accepted as an alias of update.
    /// </summary>
    /// <param name="command">The first argument.</param>
    /// <returns>The mode.</returns>
    private static RatchetMode ParseMode(string command)
    {
        return command switch
        {
            "check" => RatchetMode.Check,
            "update" or "--update" => RatchetMode.Update,
            "init" => RatchetMode.Init,
            _ => throw new QualityInputException($"Unknown command '{command}'.\n{Usage}"),
        };
    }

    /// <summary>
    /// Resolves the option values against their defaults.
    /// </summary>
    /// <param name="mode">The command.</param>
    /// <param name="values">The option values by name.</param>
    /// <param name="requireCoverage">Whether --require-coverage was given.</param>
    /// <param name="workingDirectory">The directory relative paths are resolved from.</param>
    /// <returns>The options.</returns>
    private static RatchetOptions Build(
        RatchetMode mode,
        Dictionary<string, string> values,
        bool requireCoverage,
        string workingDirectory)
    {
        string root = values.TryGetValue("--repo", out string? repo)
            ? Path.GetFullPath(repo, workingDirectory)
            : FindRepositoryRoot(workingDirectory);
        string quality = Path.Combine(root, "artifacts", "quality");
        return new RatchetOptions(
            mode,
            Path.Combine(root, SolutionFileName),
            PathOption(values, "--sarif-dir", workingDirectory, Path.Combine(quality, "sarif")),
            PathOption(values, "--coverage-dir", workingDirectory, Path.Combine(quality, "coverage")),
            PathOption(values, "--merged-coverage", workingDirectory, Path.Combine(quality, CoberturaFile.ReportFileName)),
            PathOption(values, "--baseline", workingDirectory, Path.Combine(root, "tools", "quality-baseline.json")),
            requireCoverage,
            values.TryGetValue("--libraries", out string? libraries) ? SplitList(libraries) : [],
            values.TryGetValue("--coverage-target", out string? target) ? ParsePercent(target) : 80);
    }

    /// <summary>
    /// Resolves a path option, or returns its default.
    /// </summary>
    /// <param name="values">The option values by name.</param>
    /// <param name="name">The option name.</param>
    /// <param name="workingDirectory">The directory relative paths are resolved from.</param>
    /// <param name="fallback">The absolute default path.</param>
    /// <returns>The absolute path.</returns>
    private static string PathOption(Dictionary<string, string> values, string name, string workingDirectory, string fallback)
    {
        return values.TryGetValue(name, out string? value) ? Path.GetFullPath(value, workingDirectory) : fallback;
    }

    /// <summary>
    /// Finds the nearest folder at or above the working directory that holds the solution.
    /// </summary>
    /// <param name="workingDirectory">The starting directory.</param>
    /// <returns>The repository root.</returns>
    private static string FindRepositoryRoot(string workingDirectory)
    {
        for (DirectoryInfo? directory = new(workingDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
            {
                return directory.FullName;
            }
        }

        throw new QualityInputException($"No {SolutionFileName} at or above {workingDirectory}. Pass --repo <dir>.");
    }

    /// <summary>
    /// Splits a comma-separated list, dropping empty items.
    /// </summary>
    /// <param name="list">The list.</param>
    /// <returns>The trimmed items.</returns>
    private static string[] SplitList(string list)
    {
        return list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    /// <summary>
    /// Parses a percentage between 0 and 100.
    /// </summary>
    /// <param name="text">The text of the option.</param>
    /// <returns>The percentage.</returns>
    private static double ParsePercent(string text)
    {
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && value is >= 0 and <= 100
            ? value
            : throw new QualityInputException($"--coverage-target expects a percentage between 0 and 100, got '{text}'.");
    }
}
