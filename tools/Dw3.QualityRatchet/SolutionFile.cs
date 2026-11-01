using System.Text.RegularExpressions;

namespace Dw3.QualityRatchet;

/// <summary>
/// Reads the C# projects listed in a Visual Studio solution file.
/// </summary>
internal static partial class SolutionFile
{
    /// <summary>
    /// Lists the projects of the solution whose path ends in .csproj, in solution order.
    /// </summary>
    /// <param name="solutionPath">The path of the .sln file.</param>
    /// <returns>The projects with their absolute directories.</returns>
    public static IReadOnlyList<SolutionProject> ReadProjects(string solutionPath)
    {
        if (!File.Exists(solutionPath))
        {
            throw new QualityInputException($"Solution file not found: {solutionPath}");
        }

        string solutionDirectory = Path.GetDirectoryName(Path.GetFullPath(solutionPath))!;
        return [.. ProjectLine().Matches(File.ReadAllText(solutionPath)).Select(match => ToProject(solutionDirectory, match))];
    }

    /// <summary>
    /// Builds a project from one matched solution line.
    /// </summary>
    /// <param name="solutionDirectory">The directory the project paths are relative to.</param>
    /// <param name="match">The match of <see cref="ProjectLine"/>.</param>
    /// <returns>The project with its absolute directory.</returns>
    private static SolutionProject ToProject(string solutionDirectory, Match match)
    {
        string relativePath = match.Groups["path"].Value.Replace('\\', Path.DirectorySeparatorChar);
        string projectPath = Path.GetFullPath(Path.Combine(solutionDirectory, relativePath));
        return new SolutionProject(match.Groups["name"].Value, Path.GetDirectoryName(projectPath)!);
    }

    /// <summary>
    /// Matches a solution line such as Project("{guid}") = "Name", "src\Name\Name.csproj", "{guid}".
    /// </summary>
    /// <returns>The compiled expression with the name and path groups.</returns>
    [GeneratedRegex(@"^Project\(""\{[^}]+\}""\)\s*=\s*""(?<name>[^""]+)"",\s*""(?<path>[^""]+\.csproj)""", RegexOptions.Multiline)]
    private static partial Regex ProjectLine();
}
