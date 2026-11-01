namespace Dw3.QualityRatchet;

/// <summary>
/// Merges the Cobertura reports left by the test run.
/// </summary>
internal static class CoverageMeasurer
{
    /// <summary>
    /// The command that produces the reports.
    /// </summary>
    public const string RunTestsHint =
        "Run 'dotnet test Dw3.sln --collect:\"XPlat Code Coverage\" --results-directory artifacts/quality/coverage' "
        + "after deleting artifacts/quality/coverage.";

    /// <summary>
    /// Merges every coverlet report below a directory and writes the merged report.
    /// </summary>
    /// <param name="coverageDirectory">The directory searched recursively for coverage.cobertura.xml files.</param>
    /// <param name="mergedPath">Where the merged Cobertura report is written.</param>
    /// <param name="required">Whether a missing report is an error.</param>
    /// <returns>The merged coverage, or null when no report exists and coverage is optional.</returns>
    public static CoverageReport? Measure(string coverageDirectory, string mergedPath, bool required)
    {
        List<string> reports = Directory.Exists(coverageDirectory)
            ? [.. Directory.EnumerateFiles(coverageDirectory, CoberturaFile.ReportFileName, SearchOption.AllDirectories).Order(StringComparer.Ordinal)]
            : [];
        if (reports.Count == 0)
        {
            return required
                ? throw new QualityInputException($"No Cobertura report under {coverageDirectory}. {RunTestsHint}")
                : null;
        }

        var report = new CoverageReport();
        foreach (string path in reports)
        {
            CoberturaFile.ReadInto(report, path);
        }

        CoberturaFile.Write(report, mergedPath);
        return report;
    }
}
