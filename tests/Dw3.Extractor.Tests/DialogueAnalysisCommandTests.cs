using Dw3.Formats.Dialogue;
using Dw3.Formats.Field;
using Xunit;

namespace Dw3.Extractor.Tests;

public sealed class DialogueAnalysisCommandTests
{
    [Fact]
    public void MissingDiscIsAUsageError()
    {
        using var error = new StringWriter();

        int exitCode = Cli.Run(["analyze-dialogue"], TextWriter.Null, error);

        Assert.Equal(2, exitCode);
        Assert.Contains("analyze-dialogue --disc", error.ToString());
    }

    [Fact]
    public void UnknownOptionIsAUsageError()
    {
        using var error = new StringWriter();

        int exitCode = Cli.Run(["analyze-dialogue", "--disc", "x.cue", "--bogus"], TextWriter.Null, error);

        Assert.Equal(2, exitCode);
        Assert.Contains("--bogus", error.ToString());
    }

    [Fact]
    public void UnreadableDiscIsAnActionableError()
    {
        using var error = new StringWriter();

        int exitCode = Cli.Run(["analyze-dialogue", "--disc", Path.Combine(Path.GetTempPath(), $"absent-{Guid.NewGuid():N}.cue")], TextWriter.Null, error);

        Assert.Equal(1, exitCode);
        Assert.StartsWith("Error [", error.ToString());
    }

    [Fact]
    public void ReportSummarisesStagesFailuresAndDetails()
    {
        var actor = new StageActorDialogue(0, 0x80100040, new NpcActorEntry(0, 0x80100080, 3, 0, 5, 6, 2), 24,
            [new FieldTalk([new NpcCondition(0x6001, 1)], [], 9)]);
        var result = new RomAnalysisResult(
            new ProgressConditionTables([new NpcProgressRange(1, 2)], 4, new Dictionary<int, int> { [7] = 0 }),
            [
                new StageDialogueAnalysis(0x301, 401, new StageTextFile(8, 9, "AAA/DAT/COUNTRY/XYZ/XSTALK04.BIN", 4), 0x80100000, [actor]),
                new StageDialogueAnalysis(0x303, 403, new StageTextFile(8, 9, "AAA/DAT/COUNTRY/XYZ/XSTALK04.BIN", 4), null, []),
            ],
            [new StageAnalysisException(0x302, 402, new FormatException("no selector"))]);
        using var output = new StringWriter();

        DialogueAnalysisCommand.Report(result, output, details: true);

        string[] lines = output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(
            "dialogue-analysis stages=3 analysed=2 failed=1 no_actor_list=1 actors=1 talk_tables=1 progress_ranges=1 special_records=4 progress_specials=1",
            lines[0]);
        Assert.Equal("failed Stage mode 0x302 (WSTAG402): no selector", lines[1]);
        Assert.Equal(
            "{\"mode\":769,\"stage\":401,\"textSelector\":8,\"textFileId\":9,\"textFile\":\"AAA/DAT/COUNTRY/XYZ/XSTALK04.BIN\",\"talkFile\":4," +
            "\"actorList\":\"0x80100000\",\"actors\":[{\"id\":0,\"address\":\"0x80100040\",\"character\":3,\"x\":5,\"y\":6,\"dir\":2,\"width\":24," +
            "\"talkTable\":\"0x80100080\",\"talks\":[{\"entry\":9,\"conditions\":[[24577,1]],\"actions\":[]}]}]}",
            lines[2]);
        Assert.Contains("\"actorList\":null", lines[3]);
        Assert.Equal(4, lines.Length);
    }
}
