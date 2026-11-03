using Dw3.Disc;
using Xunit;

namespace Dw3.Extractor.Tests;

/// <summary>
/// Every single-domain step must turn a disc that lacks its sources, or a disc of an unsupported
/// edition, into exit code 1 with one explicit error line, and release the disc handle it opened.
/// </summary>
public sealed class ExtractionStepFailureTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"dw3-step-failure-{Guid.NewGuid():N}");

    public static TheoryData<string> Domains =>
    [
        "zones", "collision", "player", "water", "climb", "drop", "cue", "icon", "npc", "text", "font",
        "dialogue", "talk-box", "encounter", "battle", "digivolution", "battle-models", "battle-stages",
        "battle-hud", "battle-report",
    ];

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Theory]
    [MemberData(nameof(Domains))]
    public void EmptyDiscFailsWithAnExplicitMissingSourceError(string domain)
    {
        var disc = new FabricatedDisc();
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = Cli.Run(["extract", "--disc", "fabricated.cue", "--out", _root, "--only", domain], output, error, _ => disc.Open());

        Assert.Equal(1, exitCode);
        Assert.Contains("error", error.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Matches("absent|missing", error.ToString());
        Assert.DoesNotContain("domain-complete", output.ToString());
        Assert.True(disc.Opened > 0);
        Assert.Equal(0, disc.OpenHandles);
        Assert.False(File.Exists(Path.Combine(_root, "data", "dw3.sqlite")));
    }

    [Theory]
    [MemberData(nameof(Domains))]
    public void UnsupportedEditionFailsBeforeAnyPackIsWritten(string domain)
    {
        var disc = new FabricatedDisc { Unsupported = true };
        AddEveryNamedSource(disc);
        using var error = new StringWriter();

        int exitCode = Cli.Run(["extract", "--disc", "fabricated.cue", "--out", _root, "--only", domain], TextWriter.Null, error, _ => disc.Open());

        Assert.Equal(1, exitCode);
        Assert.False(File.Exists(Path.Combine(_root, "data", "dw3.sqlite")));
        Assert.Equal(0, disc.OpenHandles);
    }

    [Fact]
    public void UnknownSingleDomainIsAUsageError()
    {
        var disc = new FabricatedDisc();
        using var error = new StringWriter();

        int exitCode = Cli.Run(["extract", "--disc", "fabricated.cue", "--out", _root, "--only", "weather"], TextWriter.Null, error, _ => disc.Open());

        Assert.Equal(2, exitCode);
        Assert.Contains("Unsupported extraction domain: weather", error.ToString());
        Assert.Equal(0, disc.Opened);
    }

    [Fact]
    public void ListFilesPrintsTheSha256OfEveryMemberInPathOrder()
    {
        var disc = new FabricatedDisc()
            .Add("B/SECOND.BIN", [1, 2, 3])
            .Add("A/FIRST.BIN", []);
        using var output = new StringWriter();

        int exitCode = Cli.Run(["list-files", "--disc", "fabricated.cue"], output, TextWriter.Null, _ => disc.Open());

        Assert.Equal(0, exitCode);
        Assert.Equal(
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855  A/FIRST.BIN" + Environment.NewLine +
            "039058c6f2c0cb492c533b0a4d14ef77cc0f78abccced5287d84a1a2011cfb81  B/SECOND.BIN" + Environment.NewLine,
            output.ToString());
        Assert.Equal(0, disc.OpenHandles);
    }

    [Fact]
    public void ListFilesReportsADiscErrorWithItsCode()
    {
        using var error = new StringWriter();

        int exitCode = Cli.Run(
            ["list-files", "--disc", "fabricated.cue"],
            TextWriter.Null,
            error,
            _ => throw new DiscSourceException(DiscSourceException.FormatInvalid, "fabricated open failure"));

        Assert.Equal(1, exitCode);
        Assert.Equal($"Error [{DiscSourceException.FormatInvalid}]: {DiscSourceException.FormatInvalid}: fabricated open failure" + Environment.NewLine, error.ToString());
    }

    [Fact]
    public void ListFilesWithoutDiscIsAUsageError()
    {
        using var error = new StringWriter();

        int exitCode = Cli.Run(["list-files"], TextWriter.Null, error, _ => throw new InvalidOperationException("must not open"));

        Assert.Equal(2, exitCode);
        Assert.Contains("list-files --disc", error.ToString());
    }

    /// <summary>Adds an empty file at every fixed path a step looks up before decoding anything.</summary>
    private static void AddEveryNamedSource(FabricatedDisc disc)
    {
        foreach (string path in new[]
        {
            "SLES_039.36", "AAA/PRO/FIELDSTG.PRO", "AAA/PRO/WSTAG200.PRO", "AAA/PRO/WSTAG235.PRO",
            "AAA/DAT/FIELD/FIELDCOM.BIN", "AAA/DAT/COUNTRY/CMFONTCS.BIN", "AAA/DAT/COUNTRY/CMFONTTM.BIN",
        })
        {
            disc.Add(path, [0]);
        }
    }
}
