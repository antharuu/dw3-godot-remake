using System.Buffers.Binary;

namespace Dw3.Formats.Field;

/// <summary>Decodes NPC lookup and snapshot-condition tables from supported disc members.</summary>
public static class NpcDiscTableDecoder
{
    /// <summary>Decodes the NPC tables from the given disc members and offsets.</summary>
    public static NpcDiscTables Decode(
        ReadOnlySpan<byte> fieldStg,
        int fileEntriesOffset,
        int fileEntryCount,
        int widthsOffset,
        int widthCount,
        ReadOnlySpan<byte> executable,
        int progressRangesOffset,
        int progressRangeCount,
        int specialConditionsOffset,
        int specialConditionsLimit)
    {
        RequireRange(fieldStg.Length, fileEntriesOffset, checked(fileEntryCount * 4), "NPC file-entry table");
        RequireRange(fieldStg.Length, widthsOffset, widthCount, "NPC width table");
        ProgressConditionTables progress = ProgressConditionDecoder.Decode(
            executable, progressRangesOffset, progressRangeCount, specialConditionsOffset, specialConditionsLimit);

        var entries = new NpcFileEntry?[fileEntryCount];
        for (int index = 0; index < entries.Length; index++)
        {
            uint packed = BinaryPrimitives.ReadUInt32LittleEndian(fieldStg.Slice(fileEntriesOffset + index * 4, 4));
            if (packed != 0)
            {
                entries[index] = new NpcFileEntry((int)(packed >> 16), (int)(packed & 0xFFFF));
            }
        }

        int[] widths = [.. fieldStg.Slice(widthsOffset, widthCount).ToArray().Select(value => (int)value)];
        return new NpcDiscTables(entries, widths, progress.Ranges, progress.SpecialProgressRanges);
    }

    private static void RequireRange(int length, int offset, int count, string name)
    {
        if (offset < 0 || count < 0 || (long)offset + count > length)
        {
            throw new FormatException($"{name} is outside its disc member.");
        }
    }
}
