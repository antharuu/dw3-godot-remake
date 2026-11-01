using System.Buffers.Binary;

namespace Dw3.Formats.Field;

/// <summary>
/// Decodes the fixed-size, type-zero-terminated StageSlot records embedded in a
/// raw field overlay. Callers must supply the verified table offset from that
/// same overlay. Unsupported slot types and malformed tables fail explicitly.
/// </summary>
public static class StageSlotDecoder
{
    private const int RecordBytes = 24;
    private const int MaximumRecords = 257;
    private const ushort MaximumSupportedType = 14;

    /// <summary>Decodes a StageSlot table starting at a byte offset in an overlay.</summary>
    public static IReadOnlyList<StageSlot> Decode(ReadOnlySpan<byte> overlay, int tableOffset)
    {
        if (tableOffset < 0 || tableOffset >= overlay.Length)
            throw new FormatException("StageSlot table offset is outside the overlay.");

        var slots = new List<StageSlot>();
        for (int index = 0; index < MaximumRecords; index++)
        {
            int offset = checked(tableOffset + index * RecordBytes);
            if (offset < 0 || offset + RecordBytes > overlay.Length)
                throw new FormatException($"StageSlot table is truncated at record {index}.");

            ushort type = ReadUInt16(overlay, offset + 8);
            if (type > MaximumSupportedType)
                throw new FormatException($"Unsupported StageSlot type {type} at record {index}.");

            var slot = new StageSlot(
                ReadUInt16(overlay, offset),
                type,
                ReadUInt16(overlay, offset + 10),
                ReadUInt16(overlay, offset + 12),
                ReadUInt16(overlay, offset + 14),
                ReadUInt16(overlay, offset + 16));
            slots.Add(slot);
            if (type == 0)
                return slots;
        }

        throw new FormatException("StageSlot table has no type-zero terminator.");
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> bytes, int offset) =>
        BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(offset, sizeof(ushort)));
}

/// <summary>One disc-backed StageSlot record, including its terminating type-zero record.</summary>
public sealed record StageSlot(ushort Condition, ushort Type, ushort Argument, ushort X, ushort Y, ushort Direction);
