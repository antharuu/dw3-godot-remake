using Xunit;

namespace Dw3.QualityRatchet.Tests;

/// <summary>
/// End-to-end behaviour of the coverage check on a fabricated repository.
/// </summary>
public sealed class RatchetCommandTests : IDisposable
{
    private const string HalfCoveredBaseline = """
        {
          "coverage": { "minimumPercent": 50, "libraries": [ "Dw3.Data" ] }
        }
        """;

    private readonly TemporaryRepository _repository = new();
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();

    public RatchetCommandTests()
    {
        _repository.WriteBaseline(HalfCoveredBaseline);
        _repository.WriteCobertura("run", "Dw3.Data", "Reader.cs", (1, 1), (2, 0));
    }

    [Fact]
    public void CheckPassesWhenCoverageIsAtTheMinimum()
    {
        int exitCode = Run("check");

        Assert.Equal(0, exitCode);
        Assert.Contains("Coverage check passed", _output.ToString(), StringComparison.Ordinal);
        Assert.Contains("coverage Dw3.Data: 50.00% (minimum 50%)", _output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void CheckFailsWhenCoverageIsBelowTheMinimum()
    {
        _repository.WriteCobertura("run", "Dw3.Data", "Reader.cs", (1, 0), (2, 0), (3, 0), (4, 1));

        int exitCode = Run("check");

        Assert.Equal(1, exitCode);
        Assert.Contains("coverage Dw3.Data: 25.00% is below the 50.00% minimum", _error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void CheckFailsWhenAListedLibraryHasNoReport()
    {
        _repository.WriteBaseline("""{ "coverage": { "minimumPercent": 50, "libraries": [ "Dw3.Data", "Dw3.Disc" ] } }""");

        int exitCode = Run("check");

        Assert.Equal(1, exitCode);
        Assert.Contains("coverage Dw3.Disc: no report (minimum 50.00%)", _error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void CheckFailsWhenNoCoberturaReportExists()
    {
        Directory.Delete(_repository.CoverageDirectory, recursive: true);

        int exitCode = Run("check");

        Assert.Equal(2, exitCode);
        Assert.Contains("No Cobertura report", _error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void CheckWritesTheMergedCoberturaReport()
    {
        Run("check");

        Assert.True(File.Exists(_repository.MergedCoveragePath));
    }

    [Fact]
    public void CheckRejectsABaselineThatStillListsViolations()
    {
        _repository.WriteBaseline("""
            {
              "coverage": { "minimumPercent": 50, "libraries": [ "Dw3.Data" ] },
              "violations": []
            }
            """);

        int exitCode = Run("check");

        Assert.Equal(2, exitCode);
        Assert.Contains("violations", _error.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{ "coverage": { "minimumPercent": 120, "libraries": [ "Dw3.Data" ] } }""", "between 0 and 100")]
    [InlineData("""{ "coverage": { "minimumPercent": 80, "libraries": [] } }""", "at least one library")]
    [InlineData("""{ }""", "coverage section")]
    [InlineData("""null""", "coverage section")]
    [InlineData("""{ "coverage": """, "Cannot read baseline")]
    public void CheckRejectsAnInvalidBaseline(string json, string expected)
    {
        _repository.WriteBaseline(json);

        int exitCode = Run("check");

        Assert.Equal(2, exitCode);
        Assert.Contains(expected, _error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void CheckFailsWhenTheBaselineIsMissing()
    {
        File.Delete(_repository.BaselinePath);

        int exitCode = Run("check");

        Assert.Equal(2, exitCode);
        Assert.Contains("Baseline not found", _error.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("update")]
    [InlineData("init")]
    [InlineData("publish")]
    public void AnyOtherCommandIsAUsageError(string command)
    {
        int exitCode = Run(command);

        Assert.Equal(2, exitCode);
        Assert.Contains("Usage", _error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void NoCommandIsAUsageError()
    {
        int exitCode = Run();

        Assert.Equal(2, exitCode);
        Assert.Contains("Usage", _error.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--require-coverage")]
    [InlineData("--baseline")]
    public void AnUnknownOrIncompleteOptionIsAUsageError(string option)
    {
        int exitCode = Run("check", option);

        Assert.Equal(2, exitCode);
        Assert.Contains($"'{option}'", _error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void PathOptionsOverrideTheDefaults()
    {
        string baseline = Path.Combine(_repository.Root, "custom-baseline.json");
        File.WriteAllText(baseline, HalfCoveredBaseline);
        string merged = Path.Combine(_repository.Root, "out", "merged.xml");

        int exitCode = Run(
            "check",
            "--repo",
            _repository.Root,
            "--baseline",
            baseline,
            "--coverage-dir",
            _repository.CoverageDirectory,
            "--merged-coverage",
            merged);

        Assert.Equal(0, exitCode);
        Assert.True(File.Exists(merged));
    }

    [Fact]
    public void TheRepositoryIsFoundFromASubdirectory()
    {
        string nested = Path.Combine(_repository.Root, "src", "Dw3.Data");
        Directory.CreateDirectory(nested);

        int exitCode = new RatchetCommand(_output, _error).Run(["check"], nested);

        Assert.Equal(0, exitCode);
    }

    [Fact]
    public void AFolderOutsideAnyRepositoryIsAnInputError()
    {
        string outside = Path.Combine(Path.GetTempPath(), $"dw3-no-repo-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outside);
        try
        {
            int exitCode = new RatchetCommand(_output, _error).Run(["check"], outside);

            Assert.Equal(2, exitCode);
            Assert.Contains("No Dw3.sln", _error.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(outside);
        }
    }

    public void Dispose()
    {
        _repository.Dispose();
        _output.Dispose();
        _error.Dispose();
    }

    private int Run(params string[] arguments)
    {
        return new RatchetCommand(_output, _error).Run(arguments, _repository.Root);
    }
}
