using System.Buffers.Binary;

namespace Dw3.Formats.Field;

/// <summary>Decodes NPC lookup and snapshot-condition tables from supported disc members.</summary>
public static class NpcDiscTableDecoder
{
    /// <summary>Decodes the NPC tables from the given disc members and offsets.</summary>
    /// <param name="fieldStg">Raw FIELDSTG.PRO bytes containing the file-entry and width tables.</param>
    /// <param name="fileEntriesOffset">File offset of the NPC file-entry table within fieldStg.</param>
    /// <param name="fileEntryCount">Number of file-entry records to decode.</param>
    /// <param name="widthsOffset">File offset of the NPC width table within fieldStg.</param>
    /// <param name="widthCount">Number of width entries to decode.</param>
    /// <param name="executable">Raw disc executable bytes containing the progress tables.</param>
    /// <param name="progressLocator">Offsets and counts for the progress-range and special-condition tables.</param>
    /// <returns>Decoded NPC lookup and condition tables ready for the field extractor.</returns>
    public static NpcDiscTables Decode(
        ReadOnlySpan<byte> fieldStg,
        int fileEntriesOffset,
        int fileEntryCount,
        int widthsOffset,
        int widthCount,
        ReadOnlySpan<byte> executable,
        NpcProgressTableLocator progressLocator)
    {
        ArgumentNullException.ThrowIfNull(progressLocator);
        RequireRange(fieldStg.Length, fileEntriesOffset, checked(fileEntryCount * 4), "NPC file-entry table");
        RequireRange(fieldStg.Length, widthsOffset, widthCount, "NPC width table");
        ProgressConditionTables progress = ProgressConditionDecoder.Decode(
            executable,
            progressLocator.ProgressRangesOffset,
            progressLocator.ProgressRangeCount,
            progressLocator.SpecialConditionsOffset,
            progressLocator.SpecialConditionsLimit);

        var entries = new NpcFileEntry?[fileEntryCount];
        for (int index = 0; index < entries.Length; index++)
        {
            uint packed = BinaryPrimitives.ReadUInt32LittleEndian(fieldStg.Slice(fileEntriesOffset + (index * 4), 4));
            if (packed != 0)
            {
                entries[index] = new NpcFileEntry((int)(packed >> 16), (int)(packed & 0xFFFF));
            }
        }

        int[] widths = [.. fieldStg.Slice(widthsOffset, widthCount).ToArray().Select(value => (int)value)];
        return new NpcDiscTables(entries, widths, progress.Ranges, progress.SpecialProgressRanges);
    }

    /// <summary>Validates that <paramref name="offset"/> + <paramref name="count"/> bytes lie within <paramref name="length"/>.</summary>
    /// <param name="length">Total byte length of the containing buffer.</param>
    /// <param name="offset">Starting byte offset of the region to validate.</param>
    /// <param name="count">Byte count of the region to validate.</param>
    /// <param name="name">Human-readable name of the field, used in the error message.</param>
    /// <exception cref="FormatException">The region lies outside the buffer.</exception>
    private static void RequireRange(int length, int offset, int count, string name)
    {
        if (offset < 0 || count < 0 || (long)offset + count > length)
        {
            throw new FormatException($"{name} is outside its disc member.");
        }
    }
}
