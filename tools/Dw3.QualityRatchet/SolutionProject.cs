namespace Dw3.QualityRatchet;

/// <summary>
/// A C# project listed in the solution.
/// </summary>
/// <param name="Name">The project name, which is also the name of its SARIF log.</param>
/// <param name="Directory">The absolute directory containing the project file.</param>
internal sealed record SolutionProject(string Name, string Directory);
