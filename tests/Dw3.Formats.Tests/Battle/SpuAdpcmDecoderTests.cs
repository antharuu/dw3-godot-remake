using System.Buffers.Binary;
using Dw3.Formats.Battle;
using Xunit;

namespace Dw3.Formats.Tests.Battle;

/// <summary>Synthetic contract tests for PS1 SPU ADPCM blocks, VAB body lengths, loops, and WAV output.</summary>
public sealed class SpuAdpcmDecoderTests
{
    [Fact]
    public void Decode_ExpandsNibblesThroughTheTerminalBlock()
    {
        byte[] encoded = Block(0x0C, 1, 0x11);

        DecodedSpuAdpcmSample decoded = SpuAdpcmDecoder.Decode(encoded);

        Assert.Equal(28, decoded.PcmSamples.Length);
        Assert.All(decoded.PcmSamples, sample => Assert.Equal((short)1, sample));
        Assert.Equal(16, decoded.EncodedBytesConsumed);
        Assert.False(decoded.Loops);
        Assert.Null(decoded.LoopStartSample);
    }

    [Fact]
    public void Decode_RecordsLoopAndAcceptsOneTerminalPaddingBlock()
    {
        byte[] encoded = [.. Block(0x0C, 4, 0x11), .. Block(0x0C, 3, 0x11), .. Block(0x0C, 7, 0)];

        DecodedSpuAdpcmSample decoded = SpuAdpcmDecoder.Decode(encoded);

        Assert.True(decoded.Loops);
        Assert.Equal(0, decoded.LoopStartSample);
        Assert.Equal(56, decoded.PcmSamples.Length);
        Assert.Equal(32, decoded.EncodedBytesConsumed);
    }

    [Fact]
    public void Decode_RejectsInvalidLoopAndBodyLengthDeclarations()
    {
        Assert.Throws<FormatException>(() => SpuAdpcmDecoder.Decode(Block(0x0C, 8, 0)));
        Assert.Throws<FormatException>(() => SoundBankBodyDecoder.Decode(HeaderArchive(1, 4), [4, 0, 0, 0]));
    }

    [Fact]
    public void Decode_WritesCanonicalMonoWaveHeader()
    {
        byte[] wave = WavePcm16Encoder.EncodeMono([(short)-2, 3], 44100);

        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(wave, 0, 4));
        Assert.Equal("WAVE", System.Text.Encoding.ASCII.GetString(wave, 8, 4));
        Assert.Equal(48, wave.Length);
        Assert.Equal((short)-2, BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(44)));
        Assert.Equal((short)3, BinaryPrimitives.ReadInt16LittleEndian(wave.AsSpan(46)));
    }

    private static byte[] Block(byte header, byte flags, byte payload)
    {
        byte[] block = new byte[16];
        block[0] = header;
        block[1] = flags;
        block.AsSpan(2).Fill(payload);
        return block;
    }

    private static byte[] HeaderArchive(ushort samples, ushort units)
    {
        const int vabBytes = 0x2020;
        byte[] archive = new byte[4 + vabBytes];
        BinaryPrimitives.WriteUInt32LittleEndian(archive, 4U);
        Span<byte> vab = archive.AsSpan(4);
        BinaryPrimitives.WriteUInt32LittleEndian(vab, 0x56414270U);
        BinaryPrimitives.WriteUInt32LittleEndian(vab.Slice(12), 32U);
        BinaryPrimitives.WriteUInt16LittleEndian(vab.Slice(22), samples);
        BinaryPrimitives.WriteUInt16LittleEndian(vab.Slice(vabBytes - 510), units);
        return archive;
    }
}
