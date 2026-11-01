using System.Xml.Linq;
using Xunit;

namespace Dw3.QualityRatchet.Tests;

/// <summary>
/// Merging Cobertura reports produced by several test projects.
/// </summary>
public sealed class CoverageMergeTests : IDisposable
{
    private readonly TemporaryRepository _repository = new();

    [Fact]
    public void ALineCoveredByAnyReportIsCovered()
    {
        _repository.WriteCobertura("first", "Dw3.Formats", "Decoder.cs", (1, 1), (2, 0), (3, 0), (4, 0));
        _repository.WriteCobertura("second", "Dw3.Formats", "Decoder.cs", (1, 0), (2, 3), (3, 0), (4, 0));

        CoverageReport report = Merge();

        Assert.Equal(50.0, report.LinePercent("Dw3.Formats"));
    }

    [Fact]
    public void PackagesAreMeasuredSeparately()
    {
        _repository.WriteCobertura("first", "Dw3.Disc", "Image.cs", (1, 1));
        _repository.WriteCobertura("second", "Dw3.Data", "Pack.cs", (1, 0), (2, 1), (3, 0));

        CoverageReport report = Merge();

        Assert.Equal(100.0, report.LinePercent("Dw3.Disc"));
        Assert.Equal(33.33, report.LinePercent("Dw3.Data"));
        Assert.Null(report.LinePercent("Dw3.Extractor"));
    }

    [Fact]
    public void TheMergedReportIsCobertura()
    {
        _repository.WriteCobertura("first", "Dw3.Disc", "Image.cs", (1, 1), (2, 0));
        _repository.WriteCobertura("second", "Dw3.Disc", "Image.cs", (2, 1));
        string output = Path.Combine(_repository.Root, "merged.xml");

        CoberturaFile.Write(Merge(), output);

        XElement package = XDocument.Load(output).Descendants("package").Single();
        Assert.Equal("Dw3.Disc", package.Attribute("name")!.Value);
        Assert.Equal("1", package.Attribute("line-rate")!.Value);
        Assert.Equal(2, package.Descendants("line").Count());
    }

    [Fact]
    public void MalformedCoberturaIsAnInputError()
    {
        Directory.CreateDirectory(_repository.CoverageDirectory);
        File.WriteAllText(Path.Combine(_repository.CoverageDirectory, "coverage.cobertura.xml"), "<coverage>");

        Assert.Throws<QualityInputException>(Merge);
    }

    [Fact]
    public void SameFileUnderDifferentSourceRootsIsDeduplicatedIntoOneEntry()
    {
        // Simulate two test projects covering the same source file:
        // - Dw3.Data.Tests: source=".../src/Dw3.Data/", filename="DataPackWriter.cs"
        // - Dw3.Game.Tests:  source=".../src/",          filename="Dw3.Data/DataPackWriter.cs"
        // Both resolve to the same absolute path; only one entry must appear in the merged report.
        string repoRoot = _repository.Root;
        string sourceNarrow = Path.Combine(repoRoot, "src", "Dw3.Data") + Path.DirectorySeparatorChar;
        string sourceWide = Path.Combine(repoRoot, "src") + Path.DirectorySeparatorChar;

        // Narrow report: source = src/Dw3.Data/, filename = DataPackWriter.cs
        _repository.WriteCoberturaWithSource("narrow", "Dw3.Data", sourceNarrow, "DataPackWriter.cs", (1, 1), (2, 0), (3, 0));

        // Wide report: source = src/, filename = Dw3.Data/DataPackWriter.cs
        _repository.WriteCoberturaWithSource("wide", "Dw3.Data", sourceWide, "Dw3.Data/DataPackWriter.cs", (1, 0), (2, 1), (3, 0));

        CoverageReport report = Merge();

        // max(1,0)=1, max(0,1)=1, max(0,0)=0 → 2 covered / 3 total = 66.67%
        Assert.Equal(66.67, report.LinePercent("Dw3.Data"));

        // Only one file entry in the package (deduplication)
        Assert.Single(report.FilesOf("Dw3.Data"));
    }

    public void Dispose()
    {
        _repository.Dispose();
    }

    private CoverageReport Merge()
    {
        var report = new CoverageReport();
        foreach (string path in Directory.EnumerateFiles(_repository.CoverageDirectory, "coverage.cobertura.xml", SearchOption.AllDirectories))
        {
            CoberturaFile.ReadInto(report, path);
        }

        return report;
    }
}
