namespace Dw3.QualityRatchet;

/// <summary>
/// Orders keys by rule, project, then folder, with ordinal comparisons so the baseline file is stable.
/// </summary>
internal sealed class ViolationKeyComparer : IComparer<ViolationKey>
{
    /// <summary>
    /// Gets the shared instance.
    /// </summary>
    public static ViolationKeyComparer Instance { get; } = new();

    /// <summary>
    /// Compares two keys.
    /// </summary>
    /// <param name="x">The first key.</param>
    /// <param name="y">The second key.</param>
    /// <returns>A negative value, zero or a positive value, as defined by <see cref="IComparer{T}"/>.</returns>
    public int Compare(ViolationKey? x, ViolationKey? y)
    {
        if (x is null || y is null)
        {
            return (x is null ? 0 : 1) - (y is null ? 0 : 1);
        }

        int byRule = string.CompareOrdinal(x.Rule, y.Rule);
        if (byRule != 0)
        {
            return byRule;
        }

        int byProject = string.CompareOrdinal(x.Project, y.Project);
        return byProject != 0 ? byProject : string.CompareOrdinal(x.Folder, y.Folder);
    }
}
