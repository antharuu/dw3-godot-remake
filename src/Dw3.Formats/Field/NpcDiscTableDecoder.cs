using System.Buffers.Binary;

namespace Dw3.Formats.Field;

public sealed record NpcFileEntry(int FileNumber, int ArchiveEntry);
public sealed record NpcProgressRange(byte Minimum, byte Maximum);
public sealed record NpcDiscTables(
    IReadOnlyList<NpcFileEntry?> FileEntries,
    IReadOnlyList<int> Widths,
    IReadOnlyList<NpcProgressRange> ProgressRanges,
    IReadOnlyDictionary<int, int> SpecialProgressRanges);

/// <summary>Decodes NPC lookup and snapshot-condition tables from supported disc members.</summary>
public static class NpcDiscTableDecoder
{
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
                entries[index] = new NpcFileEntry((int)(packed >> 16), (int)(packed & 0xFFFF));
        }
        int[] widths = fieldStg.Slice(widthsOffset, widthCount).ToArray().Select(value => (int)value).ToArray();
        return new NpcDiscTables(entries, widths, progress.Ranges, progress.SpecialProgressRanges);
    }

    private static void RequireRange(int length, int offset, int count, string name)
    {
        if (offset < 0 || count < 0 || (long)offset + count > length)
            throw new FormatException($"{name} is outside its disc member.");
    }
}

/// <summary>Evaluates the legacy opening-snapshot visibility contract from numeric disc conditions.</summary>
public static class NpcSnapshotEvaluator
{
    public static bool IsVisible(
        IReadOnlyList<NpcCondition> conditions,
        int progress,
        IReadOnlyList<NpcProgressRange> progressRanges,
        IReadOnlyDictionary<int, int> specialProgressRanges)
    {
        foreach (NpcCondition condition in conditions)
        {
            int group = (condition.Code >> 8) & 0xFE;
            int argument = condition.Code & 0x1FF;
            bool expected = condition.Value != 0;
            bool holds;
            if (group == 0x60)
                holds = expected ? progress == argument : progress != argument;
            else if (group == 0x70)
            {
                if (!specialProgressRanges.TryGetValue(argument, out int rangeIndex) || rangeIndex >= progressRanges.Count)
                    holds = false;
                else
                {
                    NpcProgressRange range = progressRanges[rangeIndex];
                    bool inside = progress >= range.Minimum && progress <= range.Maximum;
                    holds = expected ? inside : !inside;
                }
            }
            else
                holds = !expected;
            if (!holds)
                return false;
        }
        return true;
    }
}
