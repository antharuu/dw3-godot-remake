using System.Text.Json;
using Xunit;

namespace Dw3.QualityRatchet.Tests;

/// <summary>
/// End-to-end behaviour of the check and update commands on a fabricated repository.
/// </summary>
public sealed class RatchetCommandTests : IDisposable
{
    private const string TwoComplexMethodsBaseline = """
        {
          "coverage": { "minimumPercent": 50, "libraries": [ "Dw3.Data" ] },
          "violations": [
            { "rule": "S3776", "project": "Dw3.Data", "folder": "Reading", "count": 2 }
          ]
        }
        """;

    private readonly TemporaryRepository _repository = new();
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();

    public RatchetCommandTests()
    {
        _repository.AddProject("Dw3.Data", "src/Dw3.Data");
        _repository.WriteBaseline(TwoComplexMethodsBaseline);
        _repository.WriteCobertura("run", "Dw3.Data", "Reader.cs", (1, 1), (2, 0));
    }

    [Fact]
    public void CheckPassesWhenCountsAreStable()
    {
        _repository.WriteSarif("Dw3.Data", ("S3776", "Reading/A.cs", 10), ("S3776", "Reading/B.cs", 20));

        int exitCode = Run("check", "--require-coverage");

        Assert.Equal(0, exitCode);
        Assert.Contains("Quality ratchet passed", _output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void CheckFailsWhenACountIncreases()
    {
        _repository.WriteSarif(
            "Dw3.Data",
            ("S3776", "Reading/A.cs", 10),
            ("S3776", "Reading/B.cs", 20),
            ("S3776", "Reading/C.cs", 30));

        int exitCode = Run("check");

        Assert.Equal(1, exitCode);
        Assert.Contains("S3776 Dw3.Data/Reading: 2 -> 3", _error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void CheckFailsWhenANewRuleAppears()
    {
        _repository.WriteSarif(
            "Dw3.Data",
            ("S3776", "Reading/A.cs", 10),
            ("S3776", "Reading/B.cs", 20),
            ("SA1600", "Reading/A.cs", 5));

        int exitCode = Run("check");

        Assert.Equal(1, exitCode);
        Assert.Contains("new SA1600 Dw3.Data/Reading: 1", _error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void CheckFailsWhenTheSamePairAppearsInAnotherFolder()
    {
        _repository.WriteSarif(
            "Dw3.Data",
            ("S3776", "Reading/A.cs", 10),
            ("S3776", "Writing/B.cs", 20));

        int exitCode = Run("check");

        Assert.Equal(1, exitCode);
        Assert.Contains("new S3776 Dw3.Data/Writing: 1", _error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void UpdateWritesADecreasedCount()
    {
        _repository.WriteSarif("Dw3.Data", ("S3776", "Reading/A.cs", 10));

        int exitCode = Run("update");

        Assert.Equal(0, exitCode);
        Assert.Equal(1, BaselineCount("S3776", "Dw3.Data", "Reading"));
    }

    [Fact]
    public void UpdateWritesOnlyRuleProjectFolderAndCountPerEntry()
    {
        _repository.WriteSarif("Dw3.Data", ("S3776", "Reading/A.cs", 10));

        Run("update");

        using JsonDocument baseline = JsonDocument.Parse(File.ReadAllText(_repository.BaselinePath));
        JsonElement entry = baseline.RootElement.GetProperty("violations")[0];
        Assert.Equal(
            ["count", "folder", "project", "rule"],
            entry.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void UpdateDropsAPairThatReachedZeroAndNamesTheRule()
    {
        _repository.WriteSarif("Dw3.Data");

        int exitCode = Run("update");

        Assert.Equal(0, exitCode);
        Assert.Null(BaselineCount("S3776", "Dw3.Data", "Reading"));
        Assert.Contains("S3776 has no violation left", _output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void UpdateRefusesAnIncreasedCountAndKeepsTheBaseline()
    {
        _repository.WriteSarif(
            "Dw3.Data",
            ("S3776", "Reading/A.cs", 10),
            ("S3776", "Reading/B.cs", 20),
            ("S3776", "Reading/C.cs", 30));

        int exitCode = Run("update");

        Assert.Equal(1, exitCode);
        Assert.Contains("Update refused", _error.ToString(), StringComparison.Ordinal);
        Assert.Equal(TwoComplexMethodsBaseline, File.ReadAllText(_repository.BaselinePath));
    }

    [Fact]
    public void UpdateAcceptsTheLegacyFlagSpelling()
    {
        _repository.WriteSarif("Dw3.Data", ("S3776", "Reading/A.cs", 10));

        int exitCode = Run("--update");

        Assert.Equal(0, exitCode);
        Assert.Equal(1, BaselineCount("S3776", "Dw3.Data", "Reading"));
    }

    [Fact]
    public void CheckFailsWhenCoverageIsBelowTheMinimum()
    {
        _repository.WriteSarif("Dw3.Data", ("S3776", "Reading/A.cs", 10), ("S3776", "Reading/B.cs", 20));
        _repository.WriteCobertura("run", "Dw3.Data", "Reader.cs", (1, 0), (2, 0), (3, 0), (4, 1));

        int exitCode = Run("check");

        Assert.Equal(1, exitCode);
        Assert.Contains("coverage Dw3.Data: 25.00% is below the 50.00% minimum", _error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void CheckPassesWhenCoverageFallsButStaysAtTheMinimum()
    {
        _repository.WriteSarif("Dw3.Data", ("S3776", "Reading/A.cs", 10), ("S3776", "Reading/B.cs", 20));
        _repository.WriteCobertura("run", "Dw3.Data", "Reader.cs", (1, 1), (2, 1), (3, 0), (4, 0));

        int exitCode = Run("check", "--require-coverage");

        Assert.Equal(0, exitCode);
        Assert.Contains("coverage Dw3.Data: 50.00% (minimum 50%)", _output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void UpdateNeverRecordsTheMeasuredCoverage()
    {
        _repository.WriteSarif("Dw3.Data", ("S3776", "Reading/A.cs", 10), ("S3776", "Reading/B.cs", 20));
        _repository.WriteCobertura("run", "Dw3.Data", "Reader.cs", (1, 1), (2, 1), (3, 1), (4, 0));

        int exitCode = Run("update");

        Assert.Equal(0, exitCode);
        using JsonDocument baseline = JsonDocument.Parse(File.ReadAllText(_repository.BaselinePath));
        JsonElement coverage = baseline.RootElement.GetProperty("coverage");
        Assert.Equal(50.0, coverage.GetProperty("minimumPercent").GetDouble());
        Assert.Equal(["Dw3.Data"], coverage.GetProperty("libraries").EnumerateArray().Select(library => library.GetString()));
        Assert.False(coverage.TryGetProperty("linePercent", out _));
    }

    [Fact]
    public void CheckFailsWhenRequiredCoverageIsMissing()
    {
        _repository.WriteSarif("Dw3.Data", ("S3776", "Reading/A.cs", 10), ("S3776", "Reading/B.cs", 20));
        Directory.Delete(_repository.CoverageDirectory, recursive: true);

        int exitCode = Run("check", "--require-coverage");

        Assert.Equal(2, exitCode);
        Assert.Contains("No Cobertura report", _error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void CheckSkipsOptionalCoverageWhenNoReportExists()
    {
        _repository.WriteSarif("Dw3.Data", ("S3776", "Reading/A.cs", 10), ("S3776", "Reading/B.cs", 20));
        Directory.Delete(_repository.CoverageDirectory, recursive: true);

        int exitCode = Run("check");

        Assert.Equal(0, exitCode);
        Assert.Contains("coverage skipped", _output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void CheckWritesTheMergedCoberturaReport()
    {
        _repository.WriteSarif("Dw3.Data", ("S3776", "Reading/A.cs", 10), ("S3776", "Reading/B.cs", 20));

        Run("check");

        Assert.True(File.Exists(_repository.MergedCoveragePath));
    }

    [Fact]
    public void CheckFailsWhenAProjectHasNoSarifLog()
    {
        int exitCode = Run("check");

        Assert.Equal(2, exitCode);
        Assert.Contains("Dw3.Data.sarif", _error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void UnknownCommandIsAUsageError()
    {
        int exitCode = Run("publish");

        Assert.Equal(2, exitCode);
        Assert.Contains("Usage", _error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void InitRefusesToOverwriteAnExistingBaseline()
    {
        _repository.WriteSarif("Dw3.Data");

        int exitCode = Run("init", "--libraries", "Dw3.Data");

        Assert.Equal(2, exitCode);
        Assert.Equal(TwoComplexMethodsBaseline, File.ReadAllText(_repository.BaselinePath));
    }

    [Fact]
    public void InitRecordsCurrentCountsAndCoverage()
    {
        File.Delete(_repository.BaselinePath);
        _repository.WriteSarif("Dw3.Data", ("S138", "Reading/A.cs", 10));

        int exitCode = Run("init", "--libraries", "Dw3.Data", "--coverage-target", "50");

        Assert.Equal(0, exitCode);
        Assert.Equal(1, BaselineCount("S138", "Dw3.Data", "Reading"));
        Assert.Equal(0, Run("check", "--require-coverage"));
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

    private int? BaselineCount(string rule, string project, string folder)
    {
        using JsonDocument baseline = JsonDocument.Parse(File.ReadAllText(_repository.BaselinePath));
        foreach (JsonElement entry in baseline.RootElement.GetProperty("violations").EnumerateArray())
        {
            if (entry.GetProperty("rule").GetString() == rule
                && entry.GetProperty("project").GetString() == project
                && entry.GetProperty("folder").GetString() == folder)
            {
                return entry.GetProperty("count").GetInt32();
            }
        }

        return null;
    }
}
