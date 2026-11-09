using System.Buffers.Binary;
using Dw3.Formats.Battle;
using Xunit;

namespace Dw3.Formats.Tests.Battle;

/// <summary>Synthetic contract tests for battle MP archives and their first VAB entries.</summary>
public sealed class SoundBankHeaderDecoderTests
{
    [Fact]
    public void Decode_ReadsTheFirstVabEntryOfAnMpArchive()
    {
        SoundBankHeader header = SoundBankHeaderDecoder.Decode(Archive(Vab()));

        Assert.Equal(7U, header.Version);
        Assert.Equal(42U, header.Id);
        Assert.Equal(0xDEA0U, header.FileSize);
        Assert.Equal((ushort)11, header.ProgramCount);
        Assert.Equal((ushort)23, header.ToneCount);
        Assert.Equal((ushort)18, header.SampleCount);
        Assert.Equal((byte)120, header.MasterVolume);
        Assert.Equal((byte)64, header.MasterPan);
    }

    [Fact]
    public void Decode_RejectsInvalidArchiveAndVabDeclarations()
    {
        Assert.Throws<FormatException>(() => SoundBankHeaderDecoder.Decode(new byte[4]));
        Assert.Throws<FormatException>(() => SoundBankHeaderDecoder.DecodeVab(new byte[31]));
        byte[] invalidMagic = Vab();
        invalidMagic[0] = 0;
        Assert.Throws<FormatException>(() => SoundBankHeaderDecoder.DecodeVab(invalidMagic));
        byte[] invalidSize = Vab();
        BinaryPrimitives.WriteUInt32LittleEndian(invalidSize.AsSpan(12), 31U);
        Assert.Throws<FormatException>(() => SoundBankHeaderDecoder.DecodeVab(invalidSize));
    }

    private static byte[] Archive(byte[] vab)
    {
        byte[] archive = new byte[8 + vab.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(archive, 8U);
        vab.CopyTo(archive, 8);
        return archive;
    }

    private static byte[] Vab()
    {
        byte[] vab = new byte[32];
        BinaryPrimitives.WriteUInt32LittleEndian(vab, 0x56414270U);
        BinaryPrimitives.WriteUInt32LittleEndian(vab.AsSpan(4), 7U);
        BinaryPrimitives.WriteUInt32LittleEndian(vab.AsSpan(8), 42U);
        BinaryPrimitives.WriteUInt32LittleEndian(vab.AsSpan(12), 0xDEA0U);
        BinaryPrimitives.WriteUInt16LittleEndian(vab.AsSpan(18), 11);
        BinaryPrimitives.WriteUInt16LittleEndian(vab.AsSpan(20), 23);
        BinaryPrimitives.WriteUInt16LittleEndian(vab.AsSpan(22), 18);
        vab[24] = 120;
        vab[25] = 64;
        return vab;
    }
}
