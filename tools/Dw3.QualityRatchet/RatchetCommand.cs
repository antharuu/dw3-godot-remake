using System.Globalization;

namespace Dw3.QualityRatchet;

/// <summary>
/// Runs the check command: merges the Cobertura reports of the last test run and compares the line
/// coverage of every library listed in the baseline with its absolute minimum.
/// Analyzer rules are not checked here: every diagnostic is a build error, so a build that passes
/// has none.
/// </summary>
internal sealed class RatchetCommand
{
    /// <summary>
    /// The exit code of a check where every listed library reaches the minimum.
    /// </summary>
    public const int Passed = 0;

    /// <summary>
    /// The exit code of a check where a listed library is below the minimum or absent from the reports.
    /// </summary>
    public const int Broken = 1;

    /// <summary>
    /// The exit code of a command whose arguments, baseline or reports are missing or unreadable.
    /// </summary>
    public const int InvalidInput = 2;

    private readonly TextWriter _output;
    private readonly TextWriter _error;

    /// <summary>
    /// Initializes a new instance of the <see cref="RatchetCommand"/> class.
    /// </summary>
    /// <param name="output">Receives the per-library summary of a passing check.</param>
    /// <param name="error">Receives the failing libraries and the input errors.</param>
    public RatchetCommand(TextWriter output, TextWriter error)
    {
        _output = output;
        _error = error;
    }

    /// <summary>
    /// Parses the arguments and runs the check.
    /// </summary>
    /// <param name="arguments">The command (only <c>check</c> exists) followed by its options.</param>
    /// <param name="workingDirectory">The directory relative paths and the repository search start from.</param>
    /// <returns><see cref="Passed"/>, <see cref="Broken"/> or <see cref="InvalidInput"/>.</returns>
    public int Run(IReadOnlyList<string> arguments, string workingDirectory)
    {
        try
        {
            return Check(RatchetOptionsParser.Parse(arguments, workingDirectory));
        }
        catch (QualityInputException exception)
        {
            _error.WriteLine(exception.Message);
            return InvalidInput;
        }
    }

    /// <summary>
    /// Loads the baseline, merges the reports and reports every library below the minimum.
    /// </summary>
    /// <param name="options">The resolved paths.</param>
    /// <returns><see cref="Passed"/> or <see cref="Broken"/>.</returns>
    /// <exception cref="QualityInputException">When the baseline or every Cobertura report is missing or unreadable.</exception>
    private int Check(RatchetOptions options)
    {
        CoverageBaseline baseline = BaselineFile.Load(options.BaselinePath).Coverage;
        CoverageReport coverage = CoverageMeasurer.Measure(options.CoverageDirectory, options.MergedCoveragePath);
        IReadOnlyList<RatchetFinding> failures = RatchetComparer.Compare(baseline, coverage);
        if (failures.Count > 0)
        {
            _error.WriteLine("Coverage check failed:");
            foreach (RatchetFinding failure in failures)
            {
                _error.WriteLine("  " + failure.Describe());
            }

            return Broken;
        }

        WriteSummary(baseline, coverage);
        return Passed;
    }

    /// <summary>
    /// Writes the measured line coverage of each listed library next to the minimum, in ordinal order.
    /// </summary>
    /// <param name="baseline">The minimum and the libraries it applies to.</param>
    /// <param name="coverage">The merged coverage, which holds every listed library once the check passed.</param>
    private void WriteSummary(CoverageBaseline baseline, CoverageReport coverage)
    {
        _output.WriteLine("Coverage check passed.");
        string minimum = baseline.MinimumPercent.ToString("0.##", CultureInfo.InvariantCulture);
        foreach (string library in baseline.Libraries.Order(StringComparer.Ordinal))
        {
            string actual = (coverage.LinePercent(library) ?? 0).ToString("0.00", CultureInfo.InvariantCulture);
            _output.WriteLine($"  coverage {library}: {actual}% (minimum {minimum}%)");
        }
    }
}
