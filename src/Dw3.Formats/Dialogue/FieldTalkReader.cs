using System.Buffers.Binary;
using Dw3.Formats.Field;

namespace Dw3.Formats.Dialogue;

/// <summary>
/// Reads an actor's FieldTalk table from a stage overlay.
/// </summary>
/// <remarks>
/// A table is a run of 12-byte little-endian records <c>{u32 conditions, u32 actions, s32 entry}</c>
/// ended by an all-zero record. The two pointers address (code, value) pair lists ended by code
/// <c>0xFFFF</c>, or are 0 when absent. Every pointer must stay inside the overlay.
/// </remarks>
public static class FieldTalkReader
{
    private const int RecordBytes = 12;
    private const int MaximumTalks = 64;

    /// <summary>Reads the table at <paramref name="tableAddress"/>; a null pointer is an actor without talks.</summary>
    /// <exception cref="FormatException">A record or list leaves the overlay, or the sentinel is missing.</exception>
    public static IReadOnlyList<FieldTalk> Read(ReadOnlySpan<byte> overlay, uint tableAddress, uint overlayLoadAddress)
    {
        if (tableAddress == 0)
        {
            return [];
        }

        if (tableAddress < overlayLoadAddress || tableAddress - overlayLoadAddress > (uint)overlay.Length)
        {
            throw new FormatException($"FieldTalk table 0x{tableAddress:X8} is outside the overlay.");
        }

        int offset = (int)(tableAddress - overlayLoadAddress);
        var talks = new List<FieldTalk>();
        for (int index = 0; index <= MaximumTalks; index++, offset += RecordBytes)
        {
            if (offset + RecordBytes > overlay.Length)
            {
                throw new FormatException($"FieldTalk record {index} of table 0x{tableAddress:X8} extends past the overlay.");
            }

            uint conditions = BinaryPrimitives.ReadUInt32LittleEndian(overlay.Slice(offset, 4));
            uint actions = BinaryPrimitives.ReadUInt32LittleEndian(overlay.Slice(offset + 4, 4));
            int entry = BinaryPrimitives.ReadInt32LittleEndian(overlay.Slice(offset + 8, 4));
            if (conditions == 0 && actions == 0 && entry == 0)
            {
                return talks;
            }

            talks.Add(new FieldTalk(
                NpcOverlayReader.ReadConditionList(overlay, conditions, overlayLoadAddress),
                NpcOverlayReader.ReadConditionList(overlay, actions, overlayLoadAddress),
                entry));
        }

        throw new FormatException($"FieldTalk table 0x{tableAddress:X8} has no zero sentinel within {MaximumTalks} records.");
    }
}
