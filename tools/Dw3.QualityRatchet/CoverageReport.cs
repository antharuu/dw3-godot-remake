namespace Dw3.QualityRatchet;

/// <summary>
/// Line coverage merged from several Cobertura reports.
/// A line is covered when any report hit it, so the coverage of a library reached from
/// several test projects is the union of their runs.
/// </summary>
internal sealed class CoverageReport
{
    private readonly SortedDictionary<string, SortedDictionary<string, SortedDictionary<int, int>>> _hitsByPackage =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Gets the package names, which are the assembly names of the instrumented libraries.
    /// </summary>
    public IEnumerable<string> Packages => _hitsByPackage.Keys;

    /// <summary>
    /// Records the hits of one source line, keeping the highest count seen for that line.
    /// </summary>
    /// <param name="package">The package (assembly) name.</param>
    /// <param name="file">The source file name as written in the report.</param>
    /// <param name="line">The line number.</param>
    /// <param name="hits">The number of times the line ran.</param>
    public void AddLine(string package, string file, int line, int hits)
    {
        SortedDictionary<int, int> lines = LinesOf(package, file);
        lines[line] = Math.Max(hits, lines.GetValueOrDefault(line));
    }

    /// <summary>
    /// Computes the percentage of covered lines of a package, rounded to two decimals.
    /// </summary>
    /// <param name="package">The package name.</param>
    /// <returns>The percentage, or null when no report instruments the package or it has no line.</returns>
    public double? LinePercent(string package)
    {
        (int covered, int total) = LineCounts(package);
        return total == 0 ? null : Math.Round(100.0 * covered / total, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// Counts the covered and instrumented lines of a package.
    /// </summary>
    /// <param name="package">The package name.</param>
    /// <returns>The covered and total line counts, both zero for an unknown package.</returns>
    public (int Covered, int Total) LineCounts(string package)
    {
        if (!_hitsByPackage.TryGetValue(package, out var files))
        {
            return (0, 0);
        }

        List<int> hits = [.. files.Values.SelectMany(lines => lines.Values)];
        return (hits.Count(count => count > 0), hits.Count);
    }

    /// <summary>
    /// Lists the files of a package with the hits of each line, in ordinal order.
    /// </summary>
    /// <param name="package">The package name.</param>
    /// <returns>The files and their line hits.</returns>
    public IEnumerable<KeyValuePair<string, SortedDictionary<int, int>>> FilesOf(string package)
    {
        return _hitsByPackage.TryGetValue(package, out var files)
            ? files
            : Enumerable.Empty<KeyValuePair<string, SortedDictionary<int, int>>>();
    }

    /// <summary>
    /// Gets or creates the line table of a file.
    /// </summary>
    /// <param name="package">The package name.</param>
    /// <param name="file">The file name.</param>
    /// <returns>The line table.</returns>
    private SortedDictionary<int, int> LinesOf(string package, string file)
    {
        if (!_hitsByPackage.TryGetValue(package, out var files))
        {
            files = new SortedDictionary<string, SortedDictionary<int, int>>(StringComparer.Ordinal);
            _hitsByPackage[package] = files;
        }

        if (!files.TryGetValue(file, out var lines))
        {
            lines = [];
            files[file] = lines;
        }

        return lines;
    }
}
