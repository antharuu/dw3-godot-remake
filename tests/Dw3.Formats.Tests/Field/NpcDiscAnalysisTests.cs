using System.Buffers.Binary;
using Dw3.Formats.Field;
using Xunit;

namespace Dw3.Formats.Tests.Field;

public sealed class NpcDiscAnalysisTests
{
    private const uint OverlayBase = 0x800A5DE0u;
    private const uint FieldState = 0x80099D80u;

    [Fact]
    public void ActorListScannerReadsNullTerminatedPointerTable()
    {
        byte[] overlay = new byte[0x200];
        WriteInstruction(overlay, 0, Lui(8, FieldState));
        WriteInstruction(overlay, 4, Addiu(8, 8, FieldState));
        WriteInstruction(overlay, 8, Lui(9, OverlayBase + 0x80));
        WriteInstruction(overlay, 12, Ori(9, 9, OverlayBase + 0x80));
        WriteInstruction(overlay, 16, Sw(9, 8, 0x4C));
        BinaryPrimitives.WriteUInt32LittleEndian(overlay.AsSpan(0x80), OverlayBase + 0xA0);
        BinaryPrimitives.WriteUInt32LittleEndian(overlay.AsSpan(0x84), OverlayBase + 0xC0);
        BinaryPrimitives.WriteUInt32LittleEndian(overlay.AsSpan(0x88), 0);
        WriteActor(overlay, 0xA0, actorId: 21, direction: 3);
        WriteActor(overlay, 0xC0, actorId: 22, direction: 5);

        IReadOnlyList<uint> actors = NpcStageActorScanner.FindActorAddresses(overlay, OverlayBase, FieldState);

        Assert.Equal([OverlayBase + 0xA0, OverlayBase + 0xC0], actors);
    }

    [Fact]
    public void ActorListScannerRejectsAmbiguousValidStores()
    {
        byte[] overlay = new byte[0x240];
        WriteStoreSequence(overlay, 0, OverlayBase + 0x80);
        WriteStoreSequence(overlay, 24, OverlayBase + 0x100);
        BinaryPrimitives.WriteUInt32LittleEndian(overlay.AsSpan(0x80), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(overlay.AsSpan(0x100), 0);

        Assert.Throws<FormatException>(() =>
            NpcStageActorScanner.FindActorAddresses(overlay, OverlayBase, FieldState));
    }

    [Fact]
    public void DiscTablesDecodeFileEntriesWidthsAndSnapshotMetadata()
    {
        byte[] field = new byte[80];
        BinaryPrimitives.WriteUInt32LittleEndian(field.AsSpan(4), (12u << 16) | 7u);
        field[40] = 31;
        byte[] exe = new byte[80];
        exe[8] = 2;
        exe[9] = 5;
        exe[10] = 6;
        exe[11] = 9;
        exe[30] = 3;
        exe[31] = 0x30;
        exe[32] = 1;
        exe[33] = 0xFF;

        NpcDiscTables tables = NpcDiscTableDecoder.Decode(
            field,
            fileEntriesOffset: 0,
            fileEntryCount: 2,
            widthsOffset: 40,
            widthCount: 2,
            exe,
            new NpcProgressTableLocator(8, 2, 30, 40));

        Assert.Null(tables.FileEntries[0]);
        Assert.Equal(new NpcFileEntry(12, 7), tables.FileEntries[1]);
        Assert.Equal([31, 0], tables.Widths);
        Assert.Equal(new NpcProgressRange(6, 9), tables.ProgressRanges[1]);
        Assert.Equal(1, tables.SpecialProgressRanges[3]);
    }

    private static void WriteStoreSequence(byte[] bytes, int offset, uint pointer)
    {
        WriteInstruction(bytes, offset, Lui(8, FieldState));
        WriteInstruction(bytes, offset + 4, Addiu(8, 8, FieldState));
        WriteInstruction(bytes, offset + 8, Lui(9, pointer));
        WriteInstruction(bytes, offset + 12, Ori(9, 9, pointer));
        WriteInstruction(bytes, offset + 16, Sw(9, 8, 0x4C));
    }

    private static void WriteActor(byte[] overlay, int offset, short actorId, short direction)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(overlay.AsSpan(offset), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(overlay.AsSpan(offset + 4), 0);
        BinaryPrimitives.WriteInt16LittleEndian(overlay.AsSpan(offset + 8), actorId);
        BinaryPrimitives.WriteInt16LittleEndian(overlay.AsSpan(offset + 16), direction);
    }

    private static uint Lui(int rt, uint value) => (0x0Fu << 26) | ((uint)rt << 16) | ((value + 0x8000u) >> 16);

    private static uint Addiu(int rt, int rs, uint value) => (0x09u << 26) | ((uint)rs << 21) | ((uint)rt << 16) | (value & 0xFFFF);

    private static uint Ori(int rt, int rs, uint value) => (0x0Du << 26) | ((uint)rs << 21) | ((uint)rt << 16) | (value & 0xFFFF);

    private static uint Sw(int rt, int rs, short offset) => (0x2Bu << 26) | ((uint)rs << 21) | ((uint)rt << 16) | (ushort)offset;

    private static void WriteInstruction(byte[] bytes, int offset, uint word) =>
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), word);
}
