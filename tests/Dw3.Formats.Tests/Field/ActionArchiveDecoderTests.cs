using System.Buffers.Binary;
using Dw3.Formats.Field;
using Xunit;

namespace Dw3.Formats.Tests.Field;

public sealed class ActionArchiveDecoderTests
{
    [Fact]
    public void DecodesSetTableAndSequenceFromSyntheticArchive()
    {
        byte[] data = new byte[104];
        int[] offsets = [16, 40, 64, 84];
        for (int index = 0; index < offsets.Length; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(index * 4, 4), (uint)offsets[index]);
        }

        // Entry 1: one set id followed by five direction indices and the u16 terminator.
        ushort[] setWords = [0x16, 2, 2, 2, 2, 2, 0];
        for (int index = 0; index < setWords.Length; index++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(40 + index * 2, 2), setWords[index]);
        }

        // Entry 2: one record then an end terminator.
        int[] sequence = [6, 3, -16, -28, -1];
        for (int index = 0; index < sequence.Length; index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(64 + index * 4, 4), sequence[index]);
        }

        IReadOnlyList<ActionAnimationSet> sets = ActionArchiveDecoder.DecodeSetTable(data, offsets, 1);
        ActionAnimationSequence decoded = ActionArchiveDecoder.DecodeSequence(data, offsets, 2, frameCount: 7);

        ActionAnimationSet set = Assert.Single(sets);
        Assert.Equal(0x16, set.SetId);
        Assert.Equal([2, 2, 2, 2, 2], set.SequenceIndices);
        Assert.Equal(ActionSequenceTerminator.End, decoded.Terminator);
        ActionAnimationRecord record = Assert.Single(decoded.Records);
        Assert.Equal((6, 3, -16, -28), (record.Duration, record.Frame, record.OffsetX, record.OffsetY));
    }

    [Fact]
    public void RejectsSequenceWithoutFinalTerminator()
    {
        byte[] data = new byte[32];
        int[] offsets = [8, 24];
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(8, 4), 6);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(12, 4), 0);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(16, 4), 0);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(20, 4), 0);

        FormatException error = Assert.Throws<FormatException>(() => ActionArchiveDecoder.DecodeSequence(data, offsets, 0, frameCount: 1));

        Assert.Contains("terminator", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
