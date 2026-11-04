namespace Dw3.QualityRatchet;

/// <summary>
/// Parses the command line of the coverage check.
/// </summary>
internal static class RatchetOptionsParser
{
    /// <summary>
    /// The help text printed with every usage error.
    /// </summary>
    public const string Usage = """
        Usage: dotnet run --project tools/Dw3.QualityRatchet -- check [options]
          check                  fail when a listed library is below the line coverage minimum
        Options:
          --repo <dir>           repository root (default: nearest parent folder holding Dw3.sln)
          --coverage-dir <dir>   Cobertura reports (default: artifacts/quality/coverage)
          --merged-coverage <f>  merged Cobertura output (default: artifacts/quality/coverage.cobertura.xml)
          --baseline <file>      minimum and libraries (default: tools/quality-baseline.json)
        """;

    /// <summary>
    /// The solution file that marks the repository root.
    /// </summary>
    private const string SolutionFileName = "Dw3.sln";

    /// <summary>
    /// The options, all of which take a value.
    /// </summary>
    private static readonly HashSet<string> ValueOptions = new(StringComparer.Ordinal)
    {
        "--repo",
        "--coverage-dir",
        "--merged-coverage",
        "--baseline",
    };

    /// <summary>
    /// Parses the arguments.
    /// </summary>
    /// <param name="arguments">The command followed by its options.</param>
    /// <param name="workingDirectory">The directory relative paths are resolved from.</param>
    /// <returns>The options with absolute paths.</returns>
    /// <exception cref="QualityInputException">When the command is not <c>check</c>, an option is unknown or has no value, or no repository root is found.</exception>
    public static RatchetOptions Parse(IReadOnlyList<string> arguments, string workingDirectory)
    {
        if (arguments.Count == 0 || arguments[0] != "check")
        {
            string command = arguments.Count == 0 ? "no command" : $"unknown command '{arguments[0]}'";
            throw new QualityInputException($"Expected 'check', got {command}.\n{Usage}");
        }

        var pending = new Queue<string>(arguments.Skip(1));
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        while (pending.TryDequeue(out string? name))
        {
            if (!ValueOptions.Contains(name) || !pending.TryDequeue(out string? value))
            {
                throw new QualityInputException($"Unknown or incomplete option '{name}'.\n{Usage}");
            }

            values[name] = value;
        }

        return Build(values, workingDirectory);
    }

    /// <summary>
    /// Resolves the option values against their defaults under the repository root.
    /// </summary>
    /// <param name="values">The option values by name.</param>
    /// <param name="workingDirectory">The directory relative paths are resolved from.</param>
    /// <returns>The options with absolute paths.</returns>
    private static RatchetOptions Build(Dictionary<string, string> values, string workingDirectory)
    {
        string root = values.TryGetValue("--repo", out string? repo)
            ? Path.GetFullPath(repo, workingDirectory)
            : FindRepositoryRoot(workingDirectory);
        string quality = Path.Combine(root, "artifacts", "quality");
        return new RatchetOptions(
            PathOption(values, "--coverage-dir", workingDirectory, Path.Combine(quality, "coverage")),
            PathOption(values, "--merged-coverage", workingDirectory, Path.Combine(quality, CoberturaFile.ReportFileName)),
            PathOption(values, "--baseline", workingDirectory, Path.Combine(root, "tools", "quality-baseline.json")));
    }

    /// <summary>
    /// Resolves a path option, or returns its default.
    /// </summary>
    /// <param name="values">The option values by name.</param>
    /// <param name="name">The option name.</param>
    /// <param name="workingDirectory">The directory a relative value is resolved from.</param>
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
    /// <exception cref="QualityInputException">When no parent folder holds Dw3.sln.</exception>
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
}
