using System.Buffers.Binary;
using Dw3.Formats.Field;
using Xunit;

namespace Dw3.Formats.Tests.Field;

/// <summary>Unit tests for NpcOverlayReader using fabricated overlay buffers.</summary>
public sealed class NpcOverlayReaderTests
{
    // Fabricated load base matching npc_archives.py STAGE_VRAM = 0x800A5DE0
    private const uint LoadBase = 0x800A5DE0u;

    // --- ReadActorEntry ---

    [Fact]
    public void ReadActorEntry_ValidRecord_DecodesAllFields()
    {
        // Actor entry: conditions_ptr, talks_ptr, actor_id, image, x, y, direction (18 bytes)
        byte[] overlay = new byte[256];
        uint address = LoadBase + 100;
        int off = 100;
        BinaryPrimitives.WriteUInt32LittleEndian(overlay.AsSpan(off), LoadBase + 200); // conditions_ptr
        BinaryPrimitives.WriteUInt32LittleEndian(overlay.AsSpan(off + 4), LoadBase + 220); // talks_ptr
        BinaryPrimitives.WriteInt16LittleEndian(overlay.AsSpan(off + 8), 0x15);   // actor_id
        BinaryPrimitives.WriteInt16LittleEndian(overlay.AsSpan(off + 10), 3);     // image
        BinaryPrimitives.WriteInt16LittleEndian(overlay.AsSpan(off + 12), 512);   // x
        BinaryPrimitives.WriteInt16LittleEndian(overlay.AsSpan(off + 14), 768);   // y
        BinaryPrimitives.WriteInt16LittleEndian(overlay.AsSpan(off + 16), 4);     // direction

        NpcActorEntry actor = NpcOverlayReader.ReadActorEntry(overlay, address, LoadBase);

        Assert.Equal(LoadBase + 200, actor.ConditionsPtr);
        Assert.Equal(LoadBase + 220, actor.TalksPtr);
        Assert.Equal(0x15, actor.ActorId);
        Assert.Equal(3, actor.Image);
        Assert.Equal(512, actor.X);
        Assert.Equal(768, actor.Y);
        Assert.Equal(4, actor.Direction);
    }

    [Fact]
    public void ReadActorEntry_AddressBelowLoadBase_ThrowsFormatException()
    {
        byte[] overlay = new byte[256];
        Assert.Throws<FormatException>(() =>
            NpcOverlayReader.ReadActorEntry(overlay, LoadBase - 1, LoadBase));
    }

    [Fact]
    public void ReadActorEntry_RecordExtendsOutsideOverlay_ThrowsFormatException()
    {
        byte[] overlay = new byte[10];
        uint address = LoadBase; // offset = 0, but 18 bytes needed
        Assert.Throws<FormatException>(() =>
            NpcOverlayReader.ReadActorEntry(overlay, address, LoadBase));
    }

    [Fact]
    public void ReadActorEntry_DirectionAboveMax_ThrowsFormatException()
    {
        byte[] overlay = new byte[256];
        uint address = LoadBase + 50;
        int off = 50;
        BinaryPrimitives.WriteUInt32LittleEndian(overlay.AsSpan(off), 0u);
        BinaryPrimitives.WriteUInt32LittleEndian(overlay.AsSpan(off + 4), 0u);
        BinaryPrimitives.WriteInt16LittleEndian(overlay.AsSpan(off + 8), 1);
        BinaryPrimitives.WriteInt16LittleEndian(overlay.AsSpan(off + 10), 0);
        BinaryPrimitives.WriteInt16LittleEndian(overlay.AsSpan(off + 12), 0);
        BinaryPrimitives.WriteInt16LittleEndian(overlay.AsSpan(off + 14), 0);
        BinaryPrimitives.WriteInt16LittleEndian(overlay.AsSpan(off + 16), 8); // direction 8 is invalid (0..7 only)

        Assert.Throws<FormatException>(() =>
            NpcOverlayReader.ReadActorEntry(overlay, address, LoadBase));
    }

    // --- ReadConditionList ---

    [Fact]
    public void ReadConditionList_NullPtr_ReturnsEmpty()
    {
        byte[] overlay = new byte[64];
        IReadOnlyList<NpcCondition> conditions = NpcOverlayReader.ReadConditionList(overlay, 0u, LoadBase);
        Assert.Empty(conditions);
    }

    [Fact]
    public void ReadConditionList_SingleEntry_DecodesCorrectly()
    {
        byte[] overlay = new byte[256];
        // Condition at offset 100: code=0x6004, value=1, then 0xFFFF terminator
        uint ptr = LoadBase + 100;
        BinaryPrimitives.WriteUInt16LittleEndian(overlay.AsSpan(100), 0x6004);
        BinaryPrimitives.WriteUInt16LittleEndian(overlay.AsSpan(102), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(overlay.AsSpan(104), 0xFFFF);

        IReadOnlyList<NpcCondition> conditions = NpcOverlayReader.ReadConditionList(overlay, ptr, LoadBase);

        Assert.Single(conditions);
        Assert.Equal(0x6004, conditions[0].Code);
        Assert.Equal(1, conditions[0].Value);
    }

    [Fact]
    public void ReadConditionList_MultipleEntries_DecodesAll()
    {
        byte[] overlay = new byte[256];
        uint ptr = LoadBase + 50;
        // Two conditions
        BinaryPrimitives.WriteUInt16LittleEndian(overlay.AsSpan(50), 0x6001);
        BinaryPrimitives.WriteUInt16LittleEndian(overlay.AsSpan(52), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(overlay.AsSpan(54), 0x8010);
        BinaryPrimitives.WriteUInt16LittleEndian(overlay.AsSpan(56), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(overlay.AsSpan(58), 0xFFFF);

        IReadOnlyList<NpcCondition> conditions = NpcOverlayReader.ReadConditionList(overlay, ptr, LoadBase);

        Assert.Equal(2, conditions.Count);
        Assert.Equal(0x6001, conditions[0].Code);
        Assert.Equal(0, conditions[0].Value);
        Assert.Equal(0x8010, conditions[1].Code);
        Assert.Equal(1, conditions[1].Value);
    }

    [Fact]
    public void ReadConditionList_MissingTerminator_ThrowsFormatException()
    {
        // Fill overlay with non-0xFFFF values
        byte[] overlay = new byte[1200];
        uint ptr = LoadBase;
        for (int i = 0; i < overlay.Length; i += 2)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(overlay.AsSpan(i), 0x6001);
        }

        Assert.Throws<FormatException>(() =>
            NpcOverlayReader.ReadConditionList(overlay, ptr, LoadBase));
    }

    // --- ReadStageSlots ---

    [Fact]
    public void ReadStageSlots_EmptyTable_ReturnsEmpty()
    {
        byte[] overlay = new byte[256];
        uint address = LoadBase + 40;
        int off = 40;
        // First record: type=0 → terminates immediately
        // 24 bytes of zero at off (conditions u16×4 = 8, then type=0 u16 first in next 8×u16 block)
        // The slot layout: [+0..+7] = 4×u16 conditions, [+8..+23] = 8×u16 (type,arg,x,y,dir,hide,place,place_arg)
        // type is the first of the second group = overlay[off+8]
        BinaryPrimitives.WriteUInt16LittleEndian(overlay.AsSpan(off + 8), 0); // type=0

        IReadOnlyList<NpcStageSlot> slots = NpcOverlayReader.ReadStageSlots(overlay, address, LoadBase);

        Assert.Empty(slots);
    }

    [Fact]
    public void ReadStageSlots_OneSlot_DecodesCorrectly()
    {
        byte[] overlay = new byte[256];
        uint address = LoadBase;
        // Slot 0: conditions = (1,0,2,0), type=9, arg=3, x=100, y=200, dir=1
        BinaryPrimitives.WriteUInt16LittleEndian(overlay.AsSpan(0), 1);   // cond[0]
        BinaryPrimitives.WriteUInt16LittleEndian(overlay.AsSpan(2), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(overlay.AsSpan(4), 2);   // cond[2]
        BinaryPrimitives.WriteUInt16LittleEndian(overlay.AsSpan(6), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(overlay.AsSpan(8), 9);   // type
        BinaryPrimitives.WriteUInt16LittleEndian(overlay.AsSpan(10), 3);  // arg
        BinaryPrimitives.WriteUInt16LittleEndian(overlay.AsSpan(12), 100); // x
        BinaryPrimitives.WriteUInt16LittleEndian(overlay.AsSpan(14), 200); // y
        BinaryPrimitives.WriteUInt16LittleEndian(overlay.AsSpan(16), 1);  // direction
        // Terminator slot at offset 24: type=0
        BinaryPrimitives.WriteUInt16LittleEndian(overlay.AsSpan(24 + 8), 0); // type=0

        IReadOnlyList<NpcStageSlot> slots = NpcOverlayReader.ReadStageSlots(overlay, address, LoadBase);

        Assert.Single(slots);
        Assert.Equal(9, slots[0].Type);
        Assert.Equal(3, slots[0].Arg);
        Assert.Equal(100, slots[0].X);
        Assert.Equal(200, slots[0].Y);
        Assert.Equal(1, slots[0].Direction);
    }

    [Fact]
    public void ReadStageSlots_OverrunBeforeTerminator_ThrowsFormatException()
    {
        byte[] overlay = new byte[48]; // only 2 slots worth, no terminator
        uint address = LoadBase;
        // Two non-zero type slots, no room for terminator
        BinaryPrimitives.WriteUInt16LittleEndian(overlay.AsSpan(8), 1);   // type=1 in slot 0
        BinaryPrimitives.WriteUInt16LittleEndian(overlay.AsSpan(32), 1);  // type=1 in slot 1
        // No terminator fits

        Assert.Throws<FormatException>(() =>
            NpcOverlayReader.ReadStageSlots(overlay, address, LoadBase));
    }
}
