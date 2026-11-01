using Dw3.Formats.Field;
using Xunit;

namespace Dw3.Formats.Tests.Field;

public sealed class TmpkMapSelectionReaderTests
{
    [Fact]
    public void RecoversFloorAndTriggerEntriesFromBoundedSetupSequence()
    {
        uint[] words =
        [
            Move(4, 0), Lui(5, 0x0197), Ori(5, 5, 2), 0u,
            Addiu(4, 0, 7), Lui(5, 0x0197), Ori(5, 5, 1), Jalr(2), 0u,
            Addiu(4, 0, 0), Jalr(2), 0u,
        ];

        TmpkMapSelection selection = TmpkMapSelectionReader.Recover(words.SelectMany(BitConverter.GetBytes).ToArray());

        Assert.Equal(2, selection.FloorEntry);
        Assert.Equal(1, selection.TriggerEntry);
        Assert.Null(selection.AreasEntry);
    }

    [Fact]
    public void RecoversZeroFloorEntryFromUpperImmediateWithoutLowerWord()
    {
        uint[] words =
        [
            Addiu(4, 0, 0), Lui(5, 0x0339), 0u,
            Addiu(4, 0, 7), Lui(5, 0x0339), Ori(5, 5, 2), Jalr(2), 0u,
        ];

        TmpkMapSelection selection = TmpkMapSelectionReader.Recover(words.SelectMany(BitConverter.GetBytes).ToArray());

        Assert.Equal(0, selection.FloorEntry);
        Assert.Equal(2, selection.TriggerEntry);
    }

    [Fact]
    public void RecoversTriggerEntryWrittenInIndirectCallDelaySlot()
    {
        uint[] words =
        [
            Addiu(4, 0, 0), Lui(5, 0x0197), Ori(5, 5, 2), 0u,
            Addiu(4, 0, 7), Lui(5, 0x0197), 0u, Jalr(2), Ori(5, 5, 1),
        ];

        TmpkMapSelection selection = TmpkMapSelectionReader.Recover(words.SelectMany(BitConverter.GetBytes).ToArray());

        Assert.Equal(2, selection.FloorEntry);
        Assert.Equal(1, selection.TriggerEntry);
    }

    [Fact]
    public void RecoversFloorEntryWhenTheCallFollowsFullStageSetup()
    {
        uint[] words =
        [
            Addiu(4, 0, 0), Lui(5, 0x04E0),
            0u, 0u, 0u, 0u, 0u, 0u, 0u, 0u, 0u, 0u, 0u, 0u,
            Jalr(2), Ori(5, 5, 1), Addiu(4, 0, 7), Lui(5, 0x04E0), Jalr(2), Ori(5, 5, 2),
        ];

        TmpkMapSelection selection = TmpkMapSelectionReader.Recover(words.SelectMany(BitConverter.GetBytes).ToArray());

        Assert.Equal(1, selection.FloorEntry);
        Assert.Equal(2, selection.TriggerEntry);
    }

    [Fact]
    public void RecoversFloorEntryWrittenInIndirectCallDelaySlotAfterSetupInstructions()
    {
        uint[] words =
        [
            Addiu(4, 0, 0), Lui(5, 0x04E0), 0u, 0u, Jalr(2), Ori(5, 5, 1),
            Addiu(4, 0, 7), Lui(5, 0x04E0), Jalr(2), Ori(5, 5, 2),
        ];

        TmpkMapSelection selection = TmpkMapSelectionReader.Recover(words.SelectMany(BitConverter.GetBytes).ToArray());

        Assert.Equal(1, selection.FloorEntry);
        Assert.Equal(2, selection.TriggerEntry);
    }

    [Fact]
    public void RecoversFloorEntryWhenOverlayDoesNotConfigureTriggers()
    {
        uint[] words =
        [
            Addiu(4, 0, 0), Lui(5, 0x04CE), Ori(5, 5, 1), Jalr(2), 0u,
        ];

        TmpkMapSelection selection = TmpkMapSelectionReader.Recover(words.SelectMany(BitConverter.GetBytes).ToArray());

        Assert.Equal(1, selection.FloorEntry);
        Assert.False(((int?)selection.TriggerEntry).HasValue);
        Assert.Null(selection.AreasEntry);
    }

    [Fact]
    public void RecoversZeroFloorEntryFromOneBasedA0Loader()
    {
        uint[] words =
        [
            Lui(4, 0x01C7), Lui(2, 0x8004), Lw(2, 2, 0x4F5C), 0u, Jalr(2), Ori(4, 4, 1),
        ];

        TmpkMapSelection selection = TmpkMapSelectionReader.Recover(words.SelectMany(BitConverter.GetBytes).ToArray());

        Assert.Equal(0, selection.FloorEntry);
        Assert.Null(selection.TriggerEntry);
        Assert.Null(selection.AreasEntry);
    }

    [Fact]
    public void RecoversZeroFloorEntryFromOneBasedA1Descriptor()
    {
        uint[] words =
        [
            Lui(5, 0x01C9), Ori(5, 5, 1),
            Lui(7, 1), Ori(7, 7, 0x5900),
            Lui(6, 1), Ori(6, 6, 0x1700),
            Lui(8, 0x6100), Ori(8, 8, 1),
            Move(4, 0), Lui(3, 0x800A), Addiu(3, 3, unchecked((short)0x9D80)),
            Addiu(2, 0, 0x01C8), Sw(2, 3, 8), Sw(5, 3, 12),
            Addiu(5, 0, 0x0331), Jalr(2), 0u,
        ];

        TmpkMapSelection selection = TmpkMapSelectionReader.Recover(words.SelectMany(BitConverter.GetBytes).ToArray());

        Assert.Equal(0, selection.FloorEntry);
        Assert.Null(selection.TriggerEntry);
        Assert.Null(selection.AreasEntry);
    }

    [Fact]
    public void RejectsBranchInsideSetupSequence()
    {
        uint[] words =
        [
            Addiu(4, 0, 0), Lui(5, 0x0197), Ori(5, 5, 2), 0u,
            Addiu(4, 0, 7), 0x10000001u, Lui(5, 0x0197), Ori(5, 5, 1), Jalr(2), 0u,
        ];

        FormatException error = Assert.Throws<FormatException>(() => TmpkMapSelectionReader.Recover(words.SelectMany(BitConverter.GetBytes).ToArray()));

        Assert.Contains("control flow", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static uint Move(int target, int source) => ((uint)source << 21) | ((uint)target << 11) | 0x21u;
    private static uint Addiu(int target, int source, short immediate) => 0x24000000u | ((uint)source << 21) | ((uint)target << 16) | (ushort)immediate;
    private static uint Lui(int target, ushort immediate) => 0x3C000000u | ((uint)target << 16) | immediate;
    private static uint Ori(int target, int source, ushort immediate) => 0x34000000u | ((uint)source << 21) | ((uint)target << 16) | immediate;
    private static uint Lw(int target, int source, ushort immediate) => 0x8C000000u | ((uint)source << 21) | ((uint)target << 16) | immediate;
    private static uint Sw(int source, int target, ushort immediate) => 0xAC000000u | ((uint)target << 21) | ((uint)source << 16) | immediate;
    private static uint Jalr(int source) => ((uint)source << 21) | (31u << 11) | 9u;
}
