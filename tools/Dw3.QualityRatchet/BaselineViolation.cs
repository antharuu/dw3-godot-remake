using System.Text.Json.Serialization;

namespace Dw3.QualityRatchet;

/// <summary>
/// One recorded violation count of the baseline file.
/// </summary>
/// <param name="Rule">The diagnostic identifier.</param>
/// <param name="Project">The project name.</param>
/// <param name="Folder">The top-level folder inside the project.</param>
/// <param name="Count">The highest number of violations allowed for this key.</param>
internal sealed record BaselineViolation(string Rule, string Project, string Folder, int Count)
{
    /// <summary>
    /// Gets the ratchet key of this entry.
    /// </summary>
    [JsonIgnore]
    public ViolationKey Key => new(Rule, Project, Folder);
}
