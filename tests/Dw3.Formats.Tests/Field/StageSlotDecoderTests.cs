using System.Buffers.Binary;
using Dw3.Formats.Field;
using Xunit;

namespace Dw3.Formats.Tests.Field;

public sealed class StageSlotDecoderTests
{
    [Fact]
    public void DecodesTypeZeroTerminatedRecordsFromOverlayDataOffset()
    {
        byte[] overlay = new byte[72];
        WriteRecord(overlay, 0, condition: 0xffff, type: 1, arg: 0x202, x: 776, y: 224, direction: 1);
        WriteRecord(overlay, 24, condition: 0x7007, type: 4, arg: 9, x: 32, y: 48, direction: 2);
        WriteRecord(overlay, 48, condition: 0xffff, type: 0, arg: 0, x: 0, y: 0, direction: 0);

        IReadOnlyList<StageSlot> slots = StageSlotDecoder.Decode(overlay, 0);

        Assert.Collection(slots,
            slot => Assert.Equal(new StageSlot(0xffff, 1, 0x202, 776, 224, 1), slot),
            slot => Assert.Equal(new StageSlot(0x7007, 4, 9, 32, 48, 2), slot),
            slot => Assert.Equal(new StageSlot(0xffff, 0, 0, 0, 0, 0), slot));
    }

    [Fact]
    public void LocatesNoSlotsWhenOverlayDoesNotAssignTheOptionalStageSlotsMember()
    {
        byte[] overlay = new byte[12];

        IReadOnlyList<StageSlot> slots = StageSlotLocator.TryLocateAndDecode(overlay);

        Assert.Empty(slots);
    }

    [Fact]
    public void LocatesTableOnlyWhenRawMipsMaterializesOverlayPointer()
    {
        const int tableOffset = 0x40;
        byte[] overlay = new byte[0xA0];
        // lui v0, 0x800A ; addiu v0, v0, 0x5E20 ; sw v0, 0x14(sp).
        BinaryPrimitives.WriteUInt32LittleEndian(overlay.AsSpan(0), 0x3C02800Au);
        BinaryPrimitives.WriteUInt32LittleEndian(overlay.AsSpan(4), 0x24425E20u);
        BinaryPrimitives.WriteUInt32LittleEndian(overlay.AsSpan(8), 0xAFA20014u);
        // This is a referenced slot-shaped structure, but not the stageSlots
        // member assignment and must not be selected.
        BinaryPrimitives.WriteUInt32LittleEndian(overlay.AsSpan(12), 0x3C02800Au);
        BinaryPrimitives.WriteUInt32LittleEndian(overlay.AsSpan(16), 0x24425E50u);
        WriteRecord(overlay, tableOffset, condition: 0xffff, type: 1, arg: 0x202, x: 776, y: 224, direction: 1);
        WriteRecord(overlay, tableOffset + 24, condition: 0xffff, type: 0, arg: 0, x: 0, y: 0, direction: 0);
        WriteRecord(overlay, 0x70, condition: 1, type: 6, arg: 99, x: 0, y: 0, direction: 0);
        WriteRecord(overlay, 0x70 + 24, condition: 0xffff, type: 0, arg: 0, x: 0, y: 0, direction: 0);

        IReadOnlyList<StageSlot> slots = StageSlotLocator.LocateAndDecode(overlay);

        Assert.Collection(slots,
            slot => Assert.Equal(new StageSlot(0xffff, 1, 0x202, 776, 224, 1), slot),
            slot => Assert.Equal(new StageSlot(0xffff, 0, 0, 0, 0, 0), slot));
    }

    private static void WriteRecord(byte[] bytes, int offset, ushort condition, ushort type, ushort arg, ushort x, ushort y, ushort direction)
    {
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset), condition);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset + 8), type);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset + 10), arg);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset + 12), x);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset + 14), y);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(offset + 16), direction);
    }
}
