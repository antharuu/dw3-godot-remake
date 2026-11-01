using System.Buffers.Binary;

namespace Dw3.Formats.Field;

/// <summary>
/// Decodes player action archives whose final entry is a table of action sets.
/// The archive begins with little-endian u32 payload offsets. Each set is one u16
/// identifier followed by five u16 sequence indices, and the table ends at a zero
/// identifier. A sequence is four signed 32-bit words per frame and one final
/// zero (loop) or minus-one (end) word.
/// </summary>
public static class ActionArchiveDecoder
{
    /// <summary>Reads the prefix of an archive offset table through <paramref name="lastEntry"/>.</summary>
    public static int[] DecodeOffsetTable(ReadOnlySpan<byte> data, int lastEntry)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(lastEntry);
        int count = checked(lastEntry + 1);
        int tableBytes = checked(count * sizeof(uint));
        if (tableBytes > data.Length)
        {
            throw new FormatException("action archive offset table is truncated");
        }

        var offsets = new int[count];
        for (int index = 0; index < count; index++)
        {
            uint value = BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(index * sizeof(uint), sizeof(uint)));
            if (value > int.MaxValue)
            {
                throw new FormatException("action archive offset exceeds supported range");
            }

            offsets[index] = (int)value;
            if (offsets[index] < tableBytes || offsets[index] >= data.Length)
            {
                throw new FormatException("action archive offset outside payload");
            }

            if (index > 0 && offsets[index] <= offsets[index - 1])
            {
                throw new FormatException("action archive offsets are not strictly ascending");
            }
        }

        return offsets;
    }

    /// <summary>Decodes action-set records at the specified archive entry.</summary>
    public static IReadOnlyList<ActionAnimationSet> DecodeSetTable(ReadOnlySpan<byte> data, IReadOnlyList<int> offsets, int tableEntry)
    {
        ArgumentNullException.ThrowIfNull(offsets);
        if (tableEntry < 0 || tableEntry >= offsets.Count)
        {
            throw new FormatException("action set table index outside archive offsets");
        }

        int start = offsets[tableEntry];
        if (start < 0 || start > data.Length || (data.Length - start) % sizeof(ushort) != 0)
        {
            throw new FormatException("action set table is not halfword aligned");
        }

        var result = new List<ActionAnimationSet>();
        int words = (data.Length - start) / sizeof(ushort);
        for (int cursor = 0; cursor < words;)
        {
            ushort id = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(start + cursor * sizeof(ushort), sizeof(ushort)));
            if (id == 0)
            {
                return result;
            }

            if (cursor + 6 > words)
            {
                throw new FormatException("action set table is truncated");
            }

            var sequences = new int[5];
            for (int direction = 0; direction < sequences.Length; direction++)
            {
                sequences[direction] = BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(start + (cursor + direction + 1) * sizeof(ushort), sizeof(ushort)));
            }

            result.Add(new ActionAnimationSet(id, sequences));
            cursor += 6;
        }

        throw new FormatException("action set table lacks terminator");
    }

    /// <summary>Decodes one action animation sequence and validates every frame index.</summary>
    public static ActionAnimationSequence DecodeSequence(ReadOnlySpan<byte> data, IReadOnlyList<int> offsets, int index, int frameCount)
    {
        ArgumentNullException.ThrowIfNull(offsets);
        ArgumentOutOfRangeException.ThrowIfLessThan(frameCount, 1);
        if (index < 0 || index + 1 >= offsets.Count)
        {
            throw new FormatException("action sequence index outside archive offsets");
        }

        int start = offsets[index];
        int end = offsets[index + 1];
        if (start < 0 || end <= start || end > data.Length || (end - start) % sizeof(int) != 0)
        {
            throw new FormatException("action sequence payload is not word aligned");
        }

        int words = (end - start) / sizeof(int);
        var records = new List<ActionAnimationRecord>();
        for (int cursor = 0; cursor < words;)
        {
            if (!TryReadFrame(data, start, cursor, words, frameCount, out ActionAnimationRecord record))
            {
                return BuildTerminator(data, start, cursor, words, records);
            }

            records.Add(record);
            cursor += 4;
        }

        throw new FormatException("action sequence lacks terminator");
    }

    /// <summary>Returns true when the frame is not a terminator; sets record on success.</summary>
    /// <param name="data">Raw archive bytes.</param>
    /// <param name="start">Byte offset of the first word in the sequence.</param>
    /// <param name="cursor">Current word index within the sequence.</param>
    /// <param name="words">Total word count of the sequence payload.</param>
    /// <param name="frameCount">Valid TIM frame count; frame indices must be below this value.</param>
    /// <param name="record">Receives the decoded frame record on success.</param>
    /// <returns>True when the word at cursor is a positive-duration frame; false on a terminator.</returns>
    private static bool TryReadFrame(ReadOnlySpan<byte> data, int start, int cursor, int words, int frameCount, out ActionAnimationRecord record)
    {
        record = default!;
        int duration = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(start + cursor * sizeof(int), sizeof(int)));
        if (duration is 0 or -1)
        {
            return false;
        }

        if (duration < 1 || cursor + 4 > words)
        {
            throw new FormatException("action sequence has an invalid or truncated frame record");
        }

        int frame = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(start + (cursor + 1) * sizeof(int), sizeof(int)));
        if (frame < 0 || frame >= frameCount)
        {
            throw new FormatException("action sequence frame index outside archive range");
        }

        int offsetX = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(start + (cursor + 2) * sizeof(int), sizeof(int)));
        int offsetY = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(start + (cursor + 3) * sizeof(int), sizeof(int)));
        record = new ActionAnimationRecord(duration, frame, offsetX, offsetY);
        return true;
    }

    /// <summary>Builds the terminator sequence from the final word at cursor.</summary>
    /// <param name="data">Raw archive bytes.</param>
    /// <param name="start">Byte offset of the first word in the sequence.</param>
    /// <param name="cursor">Index of the terminator word.</param>
    /// <param name="words">Total word count of the payload.</param>
    /// <param name="records">Collected frame records to include in the sequence.</param>
    /// <returns>The completed sequence with its loop or end terminator.</returns>
    private static ActionAnimationSequence BuildTerminator(ReadOnlySpan<byte> data, int start, int cursor, int words, List<ActionAnimationRecord> records)
    {
        int duration = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(start + cursor * sizeof(int), sizeof(int)));
        if (cursor != words - 1)
        {
            throw new FormatException("action sequence terminator is not final");
        }

        return new ActionAnimationSequence(records, duration == 0 ? ActionSequenceTerminator.Loop : ActionSequenceTerminator.End);
    }
}
