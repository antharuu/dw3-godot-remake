using System.Globalization;

namespace Dw3.QualityRatchet;

/// <summary>
/// Runs the check, update and init commands and reports their outcome.
/// </summary>
internal sealed class RatchetCommand
{
    /// <summary>
    /// The exit code of a command whose ratchet holds.
    /// </summary>
    public const int Passed = 0;

    /// <summary>
    /// The exit code of a command that found a regression or refused an update.
    /// </summary>
    public const int Broken = 1;

    /// <summary>
    /// The exit code of a command whose input is missing or invalid.
    /// </summary>
    public const int InvalidInput = 2;

    /// <summary>
    /// The command that writes improvements into the baseline.
    /// </summary>
    private const string UpdateHint = "dotnet run --project tools/Dw3.QualityRatchet -- update";

    private readonly TextWriter _output;
    private readonly TextWriter _error;

    /// <summary>
    /// Initializes a new instance of the <see cref="RatchetCommand"/> class.
    /// </summary>
    /// <param name="output">Where progress and results are written.</param>
    /// <param name="error">Where regressions and input errors are written.</param>
    public RatchetCommand(TextWriter output, TextWriter error)
    {
        _output = output;
        _error = error;
    }

    /// <summary>
    /// Runs the command named by the first argument.
    /// </summary>
    /// <param name="arguments">The command and its options.</param>
    /// <param name="workingDirectory">The directory relative paths are resolved from.</param>
    /// <returns><see cref="Passed"/>, <see cref="Broken"/> or <see cref="InvalidInput"/>.</returns>
    public int Run(IReadOnlyList<string> arguments, string workingDirectory)
    {
        try
        {
            RatchetOptions options = RatchetOptionsParser.Parse(arguments, workingDirectory);
            return options.Mode switch
            {
                RatchetMode.Check => Check(options),
                RatchetMode.Update => Update(options),
                _ => Init(options),
            };
        }
        catch (QualityInputException exception)
        {
            _error.WriteLine(exception.Message);
            return InvalidInput;
        }
    }

    /// <summary>
    /// Writes one finding per line.
    /// </summary>
    /// <param name="writer">The destination.</param>
    /// <param name="findings">The findings.</param>
    private static void WriteFindings(TextWriter writer, IEnumerable<RatchetFinding> findings)
    {
        WriteLines(writer, findings.Select(finding => finding.Describe()));
    }

    /// <summary>
    /// Writes indented lines.
    /// </summary>
    /// <param name="writer">The destination.</param>
    /// <param name="lines">The lines.</param>
    private static void WriteLines(TextWriter writer, IEnumerable<string> lines)
    {
        foreach (string line in lines)
        {
            writer.WriteLine("  " + line);
        }
    }

    /// <summary>
    /// Fails when the measurement regresses against the baseline.
    /// </summary>
    /// <param name="options">The options.</param>
    /// <returns>The exit code.</returns>
    private int Check(RatchetOptions options)
    {
        QualityBaseline baseline = BaselineFile.Load(options.BaselinePath);
        QualityMeasurement measurement = Measure(options, options.RequireCoverage);
        RatchetComparison comparison = RatchetComparer.Compare(baseline, measurement);
        if (comparison.HasRegression)
        {
            _error.WriteLine("Quality ratchet failed. A count went up, or coverage went down:");
            WriteFindings(_error, comparison.Regressions);
            return Broken;
        }

        WriteSummary("Quality ratchet passed", baseline, measurement);
        List<RatchetFinding> improvements = [.. comparison.Improvements];
        if (improvements.Count > 0)
        {
            _output.WriteLine($"Improvements to lock in with '{UpdateHint}':");
            WriteFindings(_output, improvements);
        }

        return Passed;
    }

    /// <summary>
    /// Writes the improvements of the measurement into the baseline, refusing any regression.
    /// </summary>
    /// <param name="options">The options.</param>
    /// <returns>The exit code.</returns>
    private int Update(RatchetOptions options)
    {
        QualityBaseline baseline = BaselineFile.Load(options.BaselinePath);
        QualityMeasurement measurement = Measure(options, options.RequireCoverage);
        QualityBaseline tightened;
        try
        {
            tightened = BaselineTightener.Tighten(baseline, measurement);
        }
        catch (RatchetRefusedException refusal)
        {
            _error.WriteLine(refusal.Message);
            WriteLines(_error, refusal.Regressions);
            return Broken;
        }

        BaselineFile.Save(options.BaselinePath, tightened);
        WriteSummary("Baseline updated", tightened, measurement);
        foreach (string rule in BaselineTightener.ExhaustedRules(baseline, tightened))
        {
            _output.WriteLine(
                $"{rule} has no violation left: remove it from QualityRatchetedRules in Directory.Build.props so it becomes a build error.");
        }

        return Passed;
    }

    /// <summary>
    /// Creates the baseline from the current measurement.
    /// </summary>
    /// <param name="options">The options.</param>
    /// <returns>The exit code.</returns>
    private int Init(RatchetOptions options)
    {
        if (File.Exists(options.BaselinePath))
        {
            throw new QualityInputException($"Baseline {options.BaselinePath} already exists. Use '{UpdateHint}' to lower it.");
        }

        if (options.Libraries.Count == 0)
        {
            throw new QualityInputException("init needs --libraries <a,b> naming the libraries whose coverage is ratcheted.");
        }

        QualityMeasurement measurement = Measure(options, required: true);
        var floors = options.Libraries.ToDictionary(
            library => library,
            library => measurement.Coverage!.LinePercent(library)
                ?? throw new QualityInputException($"No coverage measured for {library}. {CoverageMeasurer.RunTestsHint}"),
            StringComparer.Ordinal);
        List<BaselineViolation> violations =
        [
            .. measurement.Violations.Select(entry => new BaselineViolation(entry.Key.Rule, entry.Key.Project, entry.Key.Folder, entry.Value)),
        ];
        var baseline = new QualityBaseline(new CoverageBaseline(options.CoverageTarget, floors), violations);
        Directory.CreateDirectory(Path.GetDirectoryName(options.BaselinePath)!);
        BaselineFile.Save(options.BaselinePath, baseline);
        WriteSummary("Baseline created", baseline, measurement);
        return Passed;
    }

    /// <summary>
    /// Reads the SARIF logs and the coverage reports.
    /// </summary>
    /// <param name="options">The options.</param>
    /// <param name="required">Whether a missing coverage report is an error.</param>
    /// <returns>The measurement.</returns>
    private QualityMeasurement Measure(RatchetOptions options, bool required)
    {
        IReadOnlyList<SolutionProject> projects = SolutionFile.ReadProjects(options.SolutionPath);
        IReadOnlyDictionary<ViolationKey, int> violations = ViolationMeasurer.Measure(projects, options.SarifDirectory);
        CoverageReport? coverage = CoverageMeasurer.Measure(options.CoverageDirectory, options.MergedCoveragePath, required);
        if (coverage is null)
        {
            _output.WriteLine($"coverage skipped: no Cobertura report under {options.CoverageDirectory}. {CoverageMeasurer.RunTestsHint}");
        }

        return new QualityMeasurement(violations, coverage);
    }

    /// <summary>
    /// Writes the totals of a measurement and the coverage of each ratcheted library.
    /// </summary>
    /// <param name="title">The first words of the summary line.</param>
    /// <param name="baseline">The baseline the coverage floors come from.</param>
    /// <param name="measurement">The measurement.</param>
    private void WriteSummary(string title, QualityBaseline baseline, QualityMeasurement measurement)
    {
        int total = measurement.Violations.Values.Sum();
        _output.WriteLine($"{title}: {total} violations in {measurement.Violations.Count} rule, project and folder groups.");
        if (measurement.Coverage is null)
        {
            return;
        }

        foreach ((string library, double floor) in baseline.Coverage.LinePercent.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            string actual = (measurement.Coverage.LinePercent(library) ?? 0).ToString("0.00", CultureInfo.InvariantCulture);
            string target = baseline.Coverage.TargetPercent.ToString("0.##", CultureInfo.InvariantCulture);
            string recorded = floor.ToString("0.00", CultureInfo.InvariantCulture);
            _output.WriteLine($"  coverage {library}: {actual}% (floor {recorded}%, target {target}%)");
        }
    }
}
