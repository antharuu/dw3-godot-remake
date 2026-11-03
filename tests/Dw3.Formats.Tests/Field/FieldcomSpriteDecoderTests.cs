using System.Buffers.Binary;
using Dw3.Formats.Field;
using Dw3.Formats.Water;
using Xunit;

namespace Dw3.Formats.Tests.Field;

public sealed class FieldcomSpriteDecoderTests
{
    [Fact]
    public void DecodesRlenVramPagesAndCompositesTheFirstListedPartOnTop()
    {
        byte[] page0 = Page(0x0021, 0x001F, 0x03E0);
        byte[] page1 = Page(0x0001, 0x7C00, 0);
        FieldcomVram vram = FieldcomVramDecoder.Decode([Rlen(page0), Rlen(page1)]);
        SpritePart top = new(0, 0, 2, 1, 0, 0, 0);
        SpritePart bottom = new(256, 0, 1, 1, 0, 0, 0);
        byte[] frame = Frame((0, 0, 0), (1, 1, 0));

        FieldcomSpriteImage image = FieldcomSpriteCompositor.Compose([top, bottom], frame, vram);

        Assert.Equal((0, 0, 2, 1, -1), (image.OffsetX, image.OffsetY, image.Width, image.Height, image.Abr));
        Assert.Equal(new ushort[] { 0x001F, 0x03E0 }, image.Words);
    }

    [Fact]
    public void RejectsAVramPageThatIsNotTheExpectedRlenTimRectangle()
    {
        FormatException error = Assert.Throws<FormatException>(() => FieldcomVramDecoder.Decode([Rlen(new byte[20])]));
        Assert.Equal("FIELDCOM VRAM page TIM header is invalid", error.Message);
    }

    [Fact]
    public void RejectsFramesReferencingUnavailablePartsOrPages()
    {
        FieldcomVram vram = FieldcomVramDecoder.Decode([Rlen(Page(0, 0, 0))]);
        Assert.Equal(
            "FIELDCOM sprite frame references missing part",
            Assert.Throws<FormatException>(() => FieldcomSpriteCompositor.Compose([], Frame((0, 0, 0)), vram)).Message);
        Assert.Equal(
            "FIELDCOM 4-bit sprite references unavailable VRAM page",
            Assert.Throws<FormatException>(() => FieldcomSpriteCompositor.Compose([new SpritePart(256, 0, 1, 1, 0, 0, 0)], Frame((0, 0, 0)), vram)).Message);
    }

    private static byte[] Frame(params (short Part, short X, short Y)[] parts)
    {
        byte[] frame = new byte[6 + (parts.Length * 6)];
        BinaryPrimitives.WriteInt16LittleEndian(frame, (short)parts.Length);
        BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(4), -1);
        for (int index = 0; index < parts.Length; index++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(6 + (index * 6)), parts[index].Part);
            BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(8 + (index * 6)), parts[index].X);
            BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(10 + (index * 6)), parts[index].Y);
        }

        return frame;
    }

    private static byte[] Page(ushort packed, ushort palette1, ushort palette2)
    {
        byte[] data = new byte[20 + (64 * 256 * 2)];
        BinaryPrimitives.WriteUInt32LittleEndian(data, 0x10);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), (uint)(data.Length - 8));
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(16), 64);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(18), 256);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(20), packed);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(22), palette1);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(24), palette2);
        return data;
    }

    private static byte[] Rlen(byte[] decoded)
    {
        var output = new List<byte>(12 + decoded.Length + (decoded.Length / 127) + 1);
        output.AddRange(BitConverter.GetBytes(8));
        output.AddRange(new byte[4]);
        output.AddRange("RLEN"u8.ToArray());
        output.AddRange(BitConverter.GetBytes(decoded.Length));
        for (int index = 0; index < decoded.Length;)
        {
            int count = Math.Min(127, decoded.Length - index);
            output.Add((byte)count);
            output.AddRange(decoded.AsSpan(index, count).ToArray());
            index += count;
        }

        output.Add(0);
        return [.. output];
    }
}
