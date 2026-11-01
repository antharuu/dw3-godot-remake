using Xunit;

namespace Dw3.QualityRatchet.Tests;

/// <summary>
/// Reading SARIF logs and grouping their results into ratchet keys.
/// </summary>
public sealed class ViolationMeasurementTests : IDisposable
{
    private readonly TemporaryRepository _repository = new();

    public ViolationMeasurementTests()
    {
        _repository.AddProject("Dw3.Game", "src/Dw3.Game");
    }

    [Fact]
    public void SuppressedAndInformationalResultsAreNotViolations()
    {
        string file = Path.Combine(_repository.ProjectDirectory("Dw3.Game"), "Host.cs");
        _repository.WriteSarifResults(
            "Dw3.Game",
            [
                TemporaryRepository.SarifResult("S3776", file, 1, "warning", suppressed: false),
                TemporaryRepository.SarifResult("CS0109", file, 2, "warning", suppressed: true),
                TemporaryRepository.SarifResult("SYSLIB1045", file, 3, "note", suppressed: false),
                TemporaryRepository.SarifResult("CA1305", file, 4, "error", suppressed: false),
            ]);

        IReadOnlyDictionary<ViolationKey, int> counts = Measure();

        Assert.Equal(2, counts.Count);
        Assert.Equal(1, counts[new ViolationKey("S3776", "Dw3.Game", ".")]);
        Assert.Equal(1, counts[new ViolationKey("CA1305", "Dw3.Game", ".")]);
    }

    [Fact]
    public void DuplicateResultsAtTheSameLocationCountOnce()
    {
        _repository.WriteSarif("Dw3.Game", ("S134", "Simulation/Step.cs", 7), ("S134", "Simulation/Step.cs", 7));

        IReadOnlyDictionary<ViolationKey, int> counts = Measure();

        Assert.Equal(1, counts[new ViolationKey("S134", "Dw3.Game", "Simulation")]);
    }

    [Fact]
    public void FilesAreGroupedByTheirTopLevelFolderInsideTheProject()
    {
        _repository.WriteSarif(
            "Dw3.Game",
            ("S138", "Presentation/Battle/Hud.cs", 1),
            ("S138", "Presentation/Field.cs", 1),
            ("S138", "GameHost.cs", 1),
            ("S138", "../Elsewhere/Shared.cs", 1));

        IReadOnlyDictionary<ViolationKey, int> counts = Measure();

        Assert.Equal(2, counts[new ViolationKey("S138", "Dw3.Game", "Presentation")]);
        Assert.Equal(1, counts[new ViolationKey("S138", "Dw3.Game", ".")]);
        Assert.Equal(1, counts[new ViolationKey("S138", "Dw3.Game", "(outside)")]);
    }

    [Fact]
    public void MalformedSarifIsAnInputError()
    {
        Directory.CreateDirectory(_repository.SarifDirectory);
        File.WriteAllText(Path.Combine(_repository.SarifDirectory, "Dw3.Game.sarif"), "{ not json");

        Assert.Throws<QualityInputException>(Measure);
    }

    public void Dispose()
    {
        _repository.Dispose();
    }

    private IReadOnlyDictionary<ViolationKey, int> Measure()
    {
        IReadOnlyList<SolutionProject> projects = SolutionFile.ReadProjects(Path.Combine(_repository.Root, "Dw3.sln"));
        return ViolationMeasurer.Measure(projects, _repository.SarifDirectory);
    }
}
