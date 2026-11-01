namespace Dw3.QualityRatchet;

/// <summary>
/// The granularity of the ratchet: a rule, a project and the top-level folder of the project.
/// </summary>
/// <param name="Rule">The diagnostic identifier.</param>
/// <param name="Project">The project name as listed in the solution.</param>
/// <param name="Folder">The first folder below the project directory, "." for files at the project root.</param>
internal sealed record ViolationKey(string Rule, string Project, string Folder)
{
    /// <summary>
    /// Formats the key as it appears in the ratchet messages, for example "S3776 Dw3.Game/Simulation".
    /// </summary>
    /// <returns>The rule, then the project and folder joined by a slash.</returns>
    public override string ToString()
    {
        return $"{Rule} {Project}/{Folder}";
    }
}
