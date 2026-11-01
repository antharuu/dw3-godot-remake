using System.Buffers.Binary;
using System.Text;
using Dw3.Formats.Binary;
using Dw3.Formats.Dialogue;
using Dw3.Formats.Field;
using Dw3.Formats.Mips;
using Xunit;
using static Dw3.Formats.Tests.Mips.MipsAssembler;

namespace Dw3.Formats.Tests.Dialogue;

/// <summary>Synthetic layouts and buffers. No byte or value comes from a game disc.</summary>
internal static class DialogueFixtures
{
    public const uint ExeLoad = 0x80020000u;
    public const uint OverlayLoad = 0x800B0000u;

    public static readonly DialogueRomLayout Layout = new()
    {
        FileSectorsAddress = ExeLoad + 0x100,
        FileSectorCount = 16,
        LanguageAddress = 0x80050010,
        LanguageCopiesPerTextFile = 3,
        FieldStateAddress = 0x80091000,
        TextFileFieldOffset = 0x20,
        ActorListFieldOffset = 0x24,
        StageOverlayLoadAddress = OverlayLoad,
        ProgressRangesAddress = ExeLoad + 0x80,
        ProgressRangeCount = 3,
        SpecialConditionsAddress = ExeLoad + 0x60,
        ActorWidthsOffset = 4,
        ActorWidthCount = 6,
    };

    /// <summary>Setup code: text = [language] + selector, actor list = <paramref name="actorList"/> (0 for none).</summary>
    public static uint[] SetupCode(int selector, uint actorList)
    {
        var words = new List<uint>
        {
            Lui(V0, Layout.LanguageAddress), LwLow(V0, V0, Layout.LanguageAddress),
            Lui(S0, Layout.FieldStateAddress), AddiuLow(S0, S0, Layout.FieldStateAddress),
            Addiu(V0, V0, selector), Sw(V0, S0, Layout.TextFileFieldOffset),
        };
        if (actorList != 0)
            words.AddRange([LuiForOri(T0, actorList), Ori(T0, T0, actorList), Sw(T0, S0, Layout.ActorListFieldOffset)]);
        words.AddRange([Jr(Ra), Nop]);
        return [.. words];
    }

    public static byte[] Overlay(int size, params uint[] code)
    {
        byte[] overlay = new byte[size];
        Code(code).CopyTo(overlay, 0);
        return overlay;
    }

    public static void U32(byte[] buffer, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(offset), value);
    public static void U16(byte[] buffer, int offset, ushort value) => BinaryPrimitives.WriteUInt16LittleEndian(buffer.AsSpan(offset), value);
    public static void S16(byte[] buffer, int offset, short value) => BinaryPrimitives.WriteInt16LittleEndian(buffer.AsSpan(offset), value);

    /// <summary>Writes an 18-byte actor entry at overlay offset <paramref name="offset"/>.</summary>
    public static void Actor(byte[] overlay, int offset, short character, short x, short y, short direction, uint talks = 0)
    {
        U32(overlay, offset, 0);
        U32(overlay, offset + 4, talks);
        S16(overlay, offset + 8, character);
        S16(overlay, offset + 12, x);
        S16(overlay, offset + 14, y);
        S16(overlay, offset + 16, direction);
    }

    /// <summary>A PS-X EXE image of <paramref name="imageBytes"/> bytes loaded at <see cref="ExeLoad"/>.</summary>
    public static byte[] Executable(int imageBytes)
    {
        byte[] exe = new byte[PsxExecutable.HeaderBytes + imageBytes];
        Encoding.ASCII.GetBytes("PS-X EXE").CopyTo(exe, 0);
        U32(exe, 0x18, ExeLoad);
        U32(exe, 0x1C, (uint)imageBytes);
        return exe;
    }

    public static int ExeOffset(uint address) => PsxExecutable.HeaderBytes + (int)(address - ExeLoad);
}

public sealed class PsxExecutableTests
{
    [Fact]
    public void MapsImageAddressesThroughTheHeaderLoadAddress()
    {
        PsxExecutable exe = PsxExecutable.Parse(DialogueFixtures.Executable(0x200));

        Assert.Equal(DialogueFixtures.ExeLoad, exe.LoadAddress);
        Assert.Equal(0x810, exe.OffsetOf(DialogueFixtures.ExeLoad + 0x10, 4));
    }

    [Theory]
    [InlineData(0x8001FFFCu, 4)]
    [InlineData(0x800201FEu, 4)]
    [InlineData(0x80020200u, 1)]
    public void RejectsRangesOutsideTheImage(uint address, int size)
    {
        PsxExecutable exe = PsxExecutable.Parse(DialogueFixtures.Executable(0x200));

        Assert.Throws<FormatException>(() => exe.OffsetOf(address, size));
    }

    [Fact]
    public void RejectsMissingMagic()
    {
        byte[] exe = DialogueFixtures.Executable(0x10);
        exe[0] = (byte)'X';

        Assert.Throws<FormatException>(() => PsxExecutable.Parse(exe));
    }

    [Fact]
    public void RejectsImageLargerThanTheFile()
    {
        byte[] exe = DialogueFixtures.Executable(0x10);
        DialogueFixtures.U32(exe, 0x1C, 0x20);

        Assert.Throws<FormatException>(() => PsxExecutable.Parse(exe));
    }
}

public sealed class ProgressConditionDecoderTests
{
    [Fact]
    public void CountsRecordsWithTerminatorAndKeepsOnlyRangeChecks()
    {
        byte[] exe = new byte[32];
        exe[0] = 4; exe[1] = 0x31; exe[2] = 1;   // range check -> range 1
        exe[3] = 5; exe[4] = 0x20; exe[5] = 9;   // other family, argument not a range index
        exe[6] = 0xFF;                           // terminator
        exe[20] = 1; exe[21] = 3; exe[22] = 6; exe[23] = 8;

        ProgressConditionTables tables = ProgressConditionDecoder.Decode(exe, 20, 2, 0, 20);

        Assert.Equal(3, tables.SpecialRecordCount);
        Assert.Equal([new NpcProgressRange(1, 3), new NpcProgressRange(6, 8)], tables.Ranges);
        Assert.Equal(1, Assert.Single(tables.SpecialProgressRanges, pair => pair.Key == 4).Value);
        Assert.False(tables.SpecialProgressRanges.ContainsKey(5));
    }

    [Fact]
    public void RejectsMissingTerminator()
    {
        byte[] exe = new byte[32];
        exe[0] = 4; exe[1] = 0x20;

        Assert.Throws<FormatException>(() => ProgressConditionDecoder.Decode(exe, 20, 2, 0, 9));
    }

    [Fact]
    public void RejectsRangeCheckBeyondTheRangeTable()
    {
        byte[] exe = new byte[32];
        exe[0] = 4; exe[1] = 0x30; exe[2] = 2; exe[3] = 0xFF;

        Assert.Throws<FormatException>(() => ProgressConditionDecoder.Decode(exe, 20, 2, 0, 20));
    }

    [Fact]
    public void RejectsRangeTableOutsideTheExecutable() =>
        Assert.Throws<FormatException>(() => ProgressConditionDecoder.Decode(new byte[32], 30, 2, 0, 20));
}

public sealed class StageTextFileResolverTests
{
    private static StageTextFileResolver Resolver(params (int FileId, string Path)[] files)
    {
        byte[] exe = DialogueFixtures.Executable(0x200);
        var paths = new Dictionary<int, string>();
        foreach ((int fileId, string path) in files)
        {
            int lba = 1000 + fileId;
            DialogueFixtures.U32(exe, DialogueFixtures.ExeOffset(DialogueFixtures.Layout.FileSectorsAddress) + fileId * 4, (uint)lba);
            paths[lba] = path;
        }
        DiscFileTable table = DiscFileTable.Read(
            PsxExecutable.Parse(exe), DialogueFixtures.Layout.FileSectorsAddress, DialogueFixtures.Layout.FileSectorCount, paths);
        return new StageTextFileResolver(table, 3, "XYZ");
    }

    [Fact]
    public void PicksTheCopyInTheCountryFolderAndReadsItsTalkNumber()
    {
        StageTextFileResolver resolver = Resolver(
            (5, "AAA/DAT/COUNTRY/ABC/ASTALK07.BIN"), (6, "AAA/DAT/COUNTRY/XYZ/XSTALK07.BIN"), (7, "AAA/DAT/COUNTRY/DEF/DSTALK07.BIN"));

        Assert.Equal(new StageTextFile(5, 6, "AAA/DAT/COUNTRY/XYZ/XSTALK07.BIN", 7), resolver.Resolve(5));
    }

    [Fact]
    public void RejectsSelectorWithoutCopyInTheFolder() =>
        Assert.Throws<FormatException>(() => Resolver((5, "AAA/DAT/COUNTRY/ABC/ASTALK07.BIN")).Resolve(5));

    [Fact]
    public void RejectsSelectorWithSeveralCopiesInTheFolder() =>
        Assert.Throws<FormatException>(() => Resolver(
            (5, "AAA/DAT/COUNTRY/XYZ/XSTALK07.BIN"), (6, "AAA/DAT/COUNTRY/XYZ/XSTALK08.BIN")).Resolve(5));

    [Fact]
    public void RejectsMemberThatIsNotATalkFile() =>
        Assert.Throws<FormatException>(() => Resolver((5, "AAA/DAT/COUNTRY/XYZ/XSSYSTEM.BIN")).Resolve(5));

    [Theory]
    [InlineData(-1)]
    [InlineData(14)]
    public void RejectsSelectorOutsideTheFileTable(int selector) =>
        Assert.Throws<FormatException>(() => Resolver().Resolve(selector));
}

public sealed class FieldTalkReaderTests
{
    private const uint Load = DialogueFixtures.OverlayLoad;

    [Fact]
    public void ReadsRecordsAndTheirPairListsUntilTheZeroRecord()
    {
        byte[] overlay = new byte[0x100];
        DialogueFixtures.U32(overlay, 0x10, Load + 0x80);
        DialogueFixtures.U32(overlay, 0x14, Load + 0x90);
        DialogueFixtures.U32(overlay, 0x18, unchecked((uint)-3));
        DialogueFixtures.U32(overlay, 0x1C, 0);
        DialogueFixtures.U32(overlay, 0x20, 0);
        DialogueFixtures.U32(overlay, 0x24, 12);
        DialogueFixtures.U16(overlay, 0x80, 0x6004); DialogueFixtures.U16(overlay, 0x82, 1); DialogueFixtures.U16(overlay, 0x84, 0xFFFF);
        DialogueFixtures.U16(overlay, 0x90, 0x0105); DialogueFixtures.U16(overlay, 0x92, 2); DialogueFixtures.U16(overlay, 0x94, 0xFFFF);

        IReadOnlyList<FieldTalk> talks = FieldTalkReader.Read(overlay, Load + 0x10, Load);

        Assert.Equal(2, talks.Count);
        Assert.Equal([new NpcCondition(0x6004, 1)], talks[0].Conditions);
        Assert.Equal([new NpcCondition(0x0105, 2)], talks[0].Actions);
        Assert.Equal(-3, talks[0].Entry);
        Assert.Empty(talks[1].Conditions);
        Assert.Equal(12, talks[1].Entry);
    }

    [Fact]
    public void NullTableIsAnActorWithoutTalks() => Assert.Empty(FieldTalkReader.Read(new byte[16], 0, Load));

    [Fact]
    public void RejectsTableWithoutSentinelBeforeTheOverlayEnd()
    {
        byte[] overlay = new byte[0x20];
        DialogueFixtures.U32(overlay, 0x18, 7);

        Assert.Throws<FormatException>(() => FieldTalkReader.Read(overlay, Load + 0x10, Load));
    }

    [Fact]
    public void RejectsTablePointerOutsideTheOverlay()
    {
        Assert.Throws<FormatException>(() => FieldTalkReader.Read(new byte[0x20], Load - 4, Load));
        Assert.Throws<FormatException>(() => FieldTalkReader.Read(new byte[0x20], Load + 0x40, Load));
    }

    [Fact]
    public void RejectsPairListWithoutTerminator()
    {
        byte[] overlay = new byte[0x40];
        DialogueFixtures.U32(overlay, 0, Load + 0x38);
        DialogueFixtures.U32(overlay, 8, 1);
        overlay.AsSpan(0x38).Fill(0x11);

        Assert.Throws<FormatException>(() => FieldTalkReader.Read(overlay, Load, Load));
    }
}

public sealed class StageDialogueScannerTests
{
    private const uint Load = DialogueFixtures.OverlayLoad;

    [Fact]
    public void RecoversTextSelectorAndValidatedActorList()
    {
        byte[] overlay = DialogueFixtures.Overlay(0x200, DialogueFixtures.SetupCode(0x2A, Load + 0x100));
        DialogueFixtures.U32(overlay, 0x100, Load + 0x140);
        DialogueFixtures.U32(overlay, 0x104, 0);
        DialogueFixtures.Actor(overlay, 0x140, character: 2, x: 10, y: 20, direction: 3);

        StageDialogueSetup setup = StageDialogueScanner.Scan(overlay, DialogueFixtures.Layout);

        Assert.Equal(0x2A, setup.TextSelector);
        Assert.NotNull(setup.ActorList);
        Assert.Equal(Load + 0x100, setup.ActorList.Address);
        Assert.Equal([Load + 0x140], setup.ActorList.Actors);
    }

    [Fact]
    public void StageWithoutActorStoreHasNoActorList()
    {
        byte[] overlay = DialogueFixtures.Overlay(0x80, DialogueFixtures.SetupCode(0x2A, 0));

        Assert.Null(StageDialogueScanner.Scan(overlay, DialogueFixtures.Layout).ActorList);
    }

    [Fact]
    public void MissingTextStoreIsADecodeError()
    {
        byte[] overlay = DialogueFixtures.Overlay(0x80, Lui(S0, DialogueFixtures.Layout.FieldStateAddress), Jr(Ra), Nop);

        Assert.Throws<MipsDecodeException>(() => StageDialogueScanner.Scan(overlay, DialogueFixtures.Layout));
    }

    [Fact]
    public void ConstantTextStoreIsNotALanguageRelativeSelector()
    {
        DialogueRomLayout layout = DialogueFixtures.Layout;
        byte[] overlay = DialogueFixtures.Overlay(0x80,
            Lui(S0, layout.FieldStateAddress), AddiuLow(S0, S0, layout.FieldStateAddress),
            Addiu(V0, Zero, 0x2A), Sw(V0, S0, layout.TextFileFieldOffset));

        Assert.Throws<MipsDecodeException>(() => StageDialogueScanner.Scan(overlay, layout));
    }

    [Fact]
    public void ConflictingSelectorsAreADecodeError()
    {
        byte[] overlay = DialogueFixtures.Overlay(0x100, [.. DialogueFixtures.SetupCode(0x2A, 0), .. DialogueFixtures.SetupCode(0x31, 0)]);

        Assert.Throws<MipsDecodeException>(() => StageDialogueScanner.Scan(overlay, DialogueFixtures.Layout));
    }

    [Fact]
    public void RepeatedIdenticalSelectorIsAccepted()
    {
        byte[] overlay = DialogueFixtures.Overlay(0x100, [.. DialogueFixtures.SetupCode(0x2A, 0), .. DialogueFixtures.SetupCode(0x2A, 0)]);

        Assert.Equal(0x2A, StageDialogueScanner.Scan(overlay, DialogueFixtures.Layout).TextSelector);
    }

    [Fact]
    public void ActorStoreToAnInvalidListIsAnError()
    {
        byte[] overlay = DialogueFixtures.Overlay(0x200, DialogueFixtures.SetupCode(0x2A, Load + 0x100));
        DialogueFixtures.U32(overlay, 0x100, Load + 0x140);
        DialogueFixtures.Actor(overlay, 0x140, character: 2, x: 0, y: 0, direction: 9);

        Assert.Throws<FormatException>(() => StageDialogueScanner.Scan(overlay, DialogueFixtures.Layout));
    }

    [Fact]
    public void ActorStoreBelowTheOverlayIsAnError()
    {
        byte[] overlay = DialogueFixtures.Overlay(0x200, DialogueFixtures.SetupCode(0x2A, Load - 0x100));

        Assert.Throws<FormatException>(() => StageDialogueScanner.Scan(overlay, DialogueFixtures.Layout));
    }
}

public sealed class RomAnalysisTests
{
    private const uint Load = DialogueFixtures.OverlayLoad;

    private static (PsxExecutable Exe, DiscFileTable Files) Executable()
    {
        DialogueRomLayout layout = DialogueFixtures.Layout;
        byte[] exe = DialogueFixtures.Executable(0x200);
        int specials = DialogueFixtures.ExeOffset(layout.SpecialConditionsAddress);
        exe[specials] = 7; exe[specials + 1] = 0x30; exe[specials + 2] = 2; exe[specials + 3] = 0xFF;
        int ranges = DialogueFixtures.ExeOffset(layout.ProgressRangesAddress);
        exe[ranges + 4] = 3; exe[ranges + 5] = 5;
        DialogueFixtures.U32(exe, DialogueFixtures.ExeOffset(layout.FileSectorsAddress) + 9 * 4, 2009);
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
            exe, DialogueFixtures.Layout, files, "XYZ", [0, 0, 0, 0, 0, 40],
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
