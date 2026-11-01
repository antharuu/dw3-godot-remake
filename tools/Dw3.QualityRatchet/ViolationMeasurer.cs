namespace Dw3.QualityRatchet;

/// <summary>
/// Counts the violations of every solution project from the SARIF logs of the last build.
/// </summary>
internal static class ViolationMeasurer
{
    /// <summary>
    /// The folder name used for files at the root of their project.
    /// </summary>
    public const string ProjectRootFolder = ".";

    /// <summary>
    /// The folder name used for files outside their project directory.
    /// </summary>
    public const string OutsideFolder = "(outside)";

    /// <summary>
    /// The folder name used for results without a source location.
    /// </summary>
    public const string NoLocationFolder = "(none)";

    /// <summary>
    /// Counts unique violations per rule, project and top-level folder.
    /// A result reported twice at the same location counts once.
    /// </summary>
    /// <param name="projects">The solution projects. Each one must have a SARIF log.</param>
    /// <param name="sarifDirectory">The directory holding one "project name".sarif log per project.</param>
    /// <returns>The counts, ordered by key.</returns>
    public static IReadOnlyDictionary<ViolationKey, int> Measure(IEnumerable<SolutionProject> projects, string sarifDirectory)
    {
        var counts = new SortedDictionary<ViolationKey, int>(ViolationKeyComparer.Instance);
        foreach (SolutionProject project in projects)
        {
            string log = Path.Combine(sarifDirectory, project.Name + ".sarif");
            if (!File.Exists(log))
            {
                throw new QualityInputException(
                    $"Missing SARIF log {log}. Run 'dotnet build Dw3.sln' first, the build writes one log per project.");
            }

            foreach (ViolationKey key in SarifLogReader.Read(log).Distinct().Select(violation => KeyOf(project, violation)))
            {
                counts[key] = counts.GetValueOrDefault(key) + 1;
            }
        }

        return counts;
    }

    /// <summary>
    /// Maps a violation to its ratchet key.
    /// </summary>
    /// <param name="project">The project whose log reported the violation.</param>
    /// <param name="violation">The violation.</param>
    /// <returns>The key.</returns>
    private static ViolationKey KeyOf(SolutionProject project, Violation violation)
    {
        return new ViolationKey(violation.RuleId, project.Name, FolderOf(project.Directory, violation.FilePath));
    }

    /// <summary>
    /// Finds the first folder of a file below a project directory.
    /// </summary>
    /// <param name="projectDirectory">The absolute project directory.</param>
    /// <param name="filePath">The absolute file path, or an empty string.</param>
    /// <returns>The folder name, or one of the reserved folder names.</returns>
    private static string FolderOf(string projectDirectory, string filePath)
    {
        if (filePath.Length == 0)
        {
            return NoLocationFolder;
        }

        string relative = Path.GetRelativePath(projectDirectory, filePath).Replace('\\', '/');
        if (relative.StartsWith("../", StringComparison.Ordinal) || Path.IsPathRooted(relative))
        {
            return OutsideFolder;
        }

        int separator = relative.IndexOf('/', StringComparison.Ordinal);
        return separator < 0 ? ProjectRootFolder : relative[..separator];
    }
}
