using System.Buffers.Binary;
using Dw3.Formats.Battle;
using Xunit;

namespace Dw3.Formats.Tests.Battle;

/// <summary>Tests the bounded European battle-overlay registry of shared three-dimensional effect models.</summary>
public sealed class EffectModelTableDecoderTests
{
    [Fact]
    public void Decode_ReadsFileAndArchiveReferencesUntilTheZeroIdTerminator()
    {
        byte[] overlay = new byte[EffectModelTableDecoder.TableOffset + (3 * EffectModelTableDecoder.EntryBytes)];
        WriteEntry(overlay, 0, 39, 0x080D001A, 0x080D0000);
        WriteEntry(overlay, 1, 65, 0x076F0004, 0x076F0000);

        IReadOnlyList<EffectModelReference> references = EffectModelTableDecoder.Decode(overlay);

        Assert.Equal(
            [
                new EffectModelReference(39, 0x80D, 0x1A, 0x80D, 0),
                new EffectModelReference(65, 0x76F, 4, 0x76F, 0),
            ],
            references);
    }

    [Fact]
    public void Decode_RejectsRegistryWithoutATerminatorInsideTheOverlay()
    {
        byte[] overlay = new byte[EffectModelTableDecoder.TableOffset + (2 * EffectModelTableDecoder.EntryBytes)];
        WriteEntry(overlay, 0, 39, 0x080D001A, 0x080D0000);
        WriteEntry(overlay, 1, 65, 0x076F0004, 0x076F0000);

        Assert.Throws<FormatException>(() => EffectModelTableDecoder.Decode(overlay));
    }

    [Fact]
    public void Decode_RejectsTruncatedRegistry()
    {
        byte[] overlay = new byte[EffectModelTableDecoder.TableOffset + EffectModelTableDecoder.EntryBytes - 1];

        Assert.Throws<FormatException>(() => EffectModelTableDecoder.Decode(overlay));
    }

    /// <summary>Repeated source rows retain one shared resource when their full references agree.</summary>
    [Fact]
    public void Decode_DeduplicatesIdenticalEffectIds()
    {
        byte[] overlay = new byte[EffectModelTableDecoder.TableOffset + (4 * EffectModelTableDecoder.EntryBytes)];
        WriteEntry(overlay, 0, 39, 0x080D001A, 0x080D0000);
        WriteEntry(overlay, 1, 39, 0x080D001A, 0x080D0000);

        Assert.Single(EffectModelTableDecoder.Decode(overlay));
    }

    private static void WriteEntry(byte[] data, int index, int id, uint model, uint motion)
    {
        int offset = EffectModelTableDecoder.TableOffset + (index * EffectModelTableDecoder.EntryBytes);
        BinaryPrimitives.WriteInt32LittleEndian(data.AsSpan(offset, 4), id);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset + 4, 4), model);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(offset + 8, 4), motion);
    }
}
