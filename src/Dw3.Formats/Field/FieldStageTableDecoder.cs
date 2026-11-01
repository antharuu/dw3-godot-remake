using System.Buffers.Binary;

namespace Dw3.Formats.Field;

/// <summary>
/// Decodes the fixed-size field-stage registry stored in the FIELDSTG.PRO disc member.
/// The table contains only mode, disc file-table index, and overlay initializer address.
/// </summary>
public static class FieldStageTableDecoder
{
    private const int RecordBytes = 12;

    /// <summary>Decodes records until the zero-mode terminator.</summary>
    public static IReadOnlyList<FieldStageTableEntry> Decode(
        ReadOnlySpan<byte> data,
        int tableOffset,
        int maximumRecords)
    {
        if (tableOffset < 0 || maximumRecords <= 0)
            throw new ArgumentOutOfRangeException(nameof(tableOffset));
        if ((long)tableOffset + (long)maximumRecords * RecordBytes > data.Length)
            throw new FormatException("Field-stage table extends outside FIELDSTG.PRO.");

        var entries = new List<FieldStageTableEntry>();
        var modes = new HashSet<ushort>();
        for (int index = 0; index < maximumRecords; index++)
        {
            int offset = tableOffset + index * RecordBytes;
            int rawMode = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, 4));
            int overlayFileId = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset + 4, 4));
            uint initializerAddress = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset + 8, 4));
            if (rawMode == 0)
                return entries;
            if (rawMode < 0 || rawMode > ushort.MaxValue)
                throw new FormatException($"Field-stage mode {rawMode} at record {index} is outside the 16-bit range.");
            if (overlayFileId < 0)
                throw new FormatException($"Field-stage overlay file id {overlayFileId} at record {index} is negative.");

            ushort mode = (ushort)rawMode;
            if (!modes.Add(mode))
                throw new FormatException($"Field-stage mode 0x{mode:X4} is duplicated.");
            entries.Add(new FieldStageTableEntry(mode, overlayFileId, initializerAddress));
        }

        throw new FormatException($"Field-stage table has no zero-mode terminator within {maximumRecords} records.");
    }
}

/// <summary>A raw record from the field-stage registry in FIELDSTG.PRO.</summary>
public sealed record FieldStageTableEntry(ushort Mode, int OverlayFileId, uint InitializerAddress);
