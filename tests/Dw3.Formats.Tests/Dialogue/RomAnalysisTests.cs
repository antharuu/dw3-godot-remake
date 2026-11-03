using Dw3.Formats.Binary;
using Dw3.Formats.Dialogue;
using Dw3.Formats.Field;
using Xunit;
using static Dw3.Formats.Tests.Mips.MipsAssembler;

namespace Dw3.Formats.Tests.Dialogue;

public sealed class RomAnalysisTests
{
    private const uint Load = DialogueFixtures.OverlayLoad;

    private static (PsxExecutable Exe, DiscFileTable Files) Executable()
    {
        DialogueRomLayout layout = DialogueFixtures.Layout;
        byte[] exe = DialogueFixtures.Executable(0x200);
        int specials = DialogueFixtures.ExeOffset(layout.SpecialConditionsAddress);
        exe[specials] = 7;
        exe[specials + 1] = 0x30;
        exe[specials + 2] = 2;
        exe[specials + 3] = 0xFF;
        int ranges = DialogueFixtures.ExeOffset(layout.ProgressRangesAddress);
        exe[ranges + 4] = 3;
        exe[ranges + 5] = 5;
        DialogueFixtures.U32(exe, DialogueFixtures.ExeOffset(layout.FileSectorsAddress) + (9 * 4), 2009);
        PsxExecutable parsed = PsxExecutable.Parse(exe);
        var paths = new Dictionary<int, string> { [2009] = "AAA/DAT/COUNTRY/XYZ/XSTALK04.BIN" };
        return (parsed, DiscFileTable.Read(parsed, layout.FileSectorsAddress, layout.FileSectorCount, paths));
    }

    [Fact]
    public void AnalysesStagesAndReportsEachFailureByMode()
    {
        (PsxExecutable exe, DiscFileTable files) = Executable();
        byte[] good = DialogueFixtures.Overlay(0x200, DialogueFixtures.SetupCode(8, Load + 0x100));
        DialogueFixtures.U32(good, 0x100, Load + 0x140);
        DialogueFixtures.Actor(good, 0x140, character: 5, x: 11, y: 22, direction: 4, talks: Load + 0x180);
        DialogueFixtures.U32(good, 0x188, 3);
        byte[] bad = DialogueFixtures.Overlay(0x40, Jr(Ra), Nop);

        RomAnalysisResult result = RomAnalysis.Analyze(
            exe,
            DialogueFixtures.Layout,
            files,
            "XYZ",
            [0, 0, 0, 0, 0, 40],
            [new StageOverlay(0x301, 401, good), new StageOverlay(0x302, 402, bad)]);

        StageDialogueAnalysis stage = Assert.Single(result.Stages);
        Assert.Equal(4, stage.TextFile.TalkIndex);
        Assert.Equal(Load + 0x100, stage.ActorListAddress);
        StageActorDialogue actor = Assert.Single(stage.Actors);
        Assert.Equal((0, Load + 0x140, 40, (short)11, (short)22), (actor.Index, actor.Address, actor.Width, actor.Entry.X, actor.Entry.Y));
        Assert.Equal(3, Assert.Single(actor.Talks).Entry);
        StageAnalysisException failure = Assert.Single(result.Failures);
        Assert.Equal((ushort)0x302, failure.Mode);
        Assert.Contains("WSTAG402", failure.Message);
        Assert.Equal(new NpcProgressRange(3, 5), result.Progress.Ranges[2]);
        Assert.Equal(2, result.Progress.SpecialRecordCount);
    }

    [Fact]
    public void ActorWhoseCharacterHasNoWidthFailsTheStage()
    {
        (PsxExecutable exe, DiscFileTable files) = Executable();
        byte[] overlay = DialogueFixtures.Overlay(0x200, DialogueFixtures.SetupCode(8, Load + 0x100));
        DialogueFixtures.U32(overlay, 0x100, Load + 0x140);
        DialogueFixtures.Actor(overlay, 0x140, character: 6, x: 0, y: 0, direction: 0);

        RomAnalysisResult result = RomAnalysis.Analyze(
            exe, DialogueFixtures.Layout, files, "XYZ", [1, 2, 3, 4, 5, 6], [new StageOverlay(0x301, 401, overlay)]);

        Assert.Empty(result.Stages);
        Assert.Contains("width table", Assert.Single(result.Failures).Message);
    }

    [Fact]
    public void MissingProgressTerminatorAbortsTheAnalysis()
    {
        DialogueRomLayout layout = DialogueFixtures.Layout;
        byte[] raw = DialogueFixtures.Executable(0x200);
        raw.AsSpan(DialogueFixtures.ExeOffset(layout.SpecialConditionsAddress), 0x20).Fill(0x01);
        PsxExecutable exe = PsxExecutable.Parse(raw);
        DiscFileTable files = DiscFileTable.Read(exe, layout.FileSectorsAddress, layout.FileSectorCount, new Dictionary<int, string>());

        Assert.Throws<FormatException>(() => RomAnalysis.Analyze(exe, layout, files, "XYZ", [1], []));
    }
}
