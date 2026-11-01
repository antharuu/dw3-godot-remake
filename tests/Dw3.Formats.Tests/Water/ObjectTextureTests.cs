using System.Buffers.Binary;
using Dw3.Formats.Water;
using Xunit;

namespace Dw3.Formats.Tests.Water;

public sealed class ObjectTextureArchiveDecoderTests
{
    [Fact]
    public void LoadsEveryImageEntryAsAVramPageAndEntryZeroClutAsAlternatePalettes()
    {
        ushort[] clut = new ushort[256 * 2];
        clut[256 + 7] = 0x7C00;
        ushort[] first = new ushort[64 * 256];
        first[0] = 0x1234;
        ushort[] second = new ushort[64 * 256];
        second[64 * 255 + 63] = 0xBEEF;
        byte[] archive = BuildArchive(Tim(clut, 2, first), Tim(null, 0, second));

        ObjectVram vram = ObjectTextureArchiveDecoder.Decode(archive);

        Assert.Equal(2, vram.Pages.Count);
        Assert.Equal(0x1234, vram.Pages[0][0]);
        Assert.Equal(0xBEEF, vram.Pages[1][64 * 255 + 63]);
        Assert.Equal(2, vram.AlternatePalettes.Count);
        Assert.Equal(0x7C00, vram.AlternatePalettes[1][7]);
    }

    [Fact]
    public void IgnoresTheClutOfEntriesAfterEntryZero()
    {
        byte[] archive = BuildArchive(Tim(null, 0, new ushort[64 * 256]), Tim(new ushort[256], 1, new ushort[64 * 256]));

        Assert.Empty(ObjectTextureArchiveDecoder.Decode(archive).AlternatePalettes);
    }

    [Fact]
    public void RejectsAnEntryThatIsNotATim()
    {
        byte[] archive = BuildArchive(new byte[24]);

        FormatException error = Assert.Throws<FormatException>(() => ObjectTextureArchiveDecoder.Decode(archive));
        Assert.Equal("object archive entry=0 is not a packed TIM", error.Message);
    }

    [Fact]
    public void RejectsAnImageRectangleOtherThanOneVramPage()
    {
        byte[] archive = BuildArchive(Tim(null, 0, new ushort[64 * 128], height: 128));

        FormatException error = Assert.Throws<FormatException>(() => ObjectTextureArchiveDecoder.Decode(archive));
        Assert.Equal("object archive image rectangle invalid", error.Message);
    }

    [Fact]
    public void RejectsUnterminatedOrDisorderedOffsetTables()
    {
        Assert.Equal("archive offset table unterminated",
            Assert.Throws<FormatException>(() => ObjectTextureArchiveDecoder.Decode(new byte[2])).Message);
        Assert.Equal("archive offset outside payload",
            Assert.Throws<FormatException>(() => ObjectTextureArchiveDecoder.Decode(SpriteSheetDecoderTests.U32(4))).Message);
        byte[] disordered = [.. SpriteSheetDecoderTests.U32(16), .. SpriteSheetDecoderTests.U32(12), .. SpriteSheetDecoderTests.U32(0), 0, 0, 0, 0, 0, 0, 0, 0];
        Assert.Equal("archive offsets invalid",
            Assert.Throws<FormatException>(() => ObjectTextureArchiveDecoder.Decode(disordered)).Message);
        Assert.Equal("archive offsets invalid",
            Assert.Throws<FormatException>(() => ObjectTextureArchiveDecoder.Decode(SpriteSheetDecoderTests.U32(0))).Message);
    }

    internal static byte[] BuildArchive(params byte[][] entries)
    {
        int header = 4 * (entries.Length + 1);
        var buffer = new List<byte>();
        int cursor = header;
        foreach (byte[] entry in entries)
        {
            buffer.AddRange(SpriteSheetDecoderTests.U32(cursor));
            cursor += entry.Length;
        }
        buffer.AddRange(SpriteSheetDecoderTests.U32(0));
        foreach (byte[] entry in entries)
            buffer.AddRange(entry);
        return [.. buffer];
    }

    internal static byte[] Tim(ushort[]? clut, int clutRows, ushort[] image, int height = 256)
    {
        var buffer = new List<byte>();
        buffer.AddRange(SpriteSheetDecoderTests.U32(0x10));
        buffer.AddRange(SpriteSheetDecoderTests.U32(clut is null ? 0 : 8));
        if (clut is not null)
            AddBlock(buffer, 256, clutRows, clut);
        AddBlock(buffer, 64, height, image);
        return [.. buffer];
    }

    private static void AddBlock(List<byte> buffer, int width, int height, ushort[] words)
    {
        buffer.AddRange(SpriteSheetDecoderTests.U32(12 + words.Length * 2));
        foreach (ushort value in new ushort[] { 0, 0, (ushort)width, (ushort)height })
            buffer.AddRange(BitConverter.GetBytes(value));
        foreach (ushort word in words)
            buffer.AddRange(BitConverter.GetBytes(word));
    }
}

public sealed class SpriteFrameRendererTests
{
    [Fact]
    public void SamplesFourBitAndEightBitPartsFromTheirBoundVramPages()
    {
        ObjectVram vram = SelfTestVram();
        byte[] frame = Frame(abr: -1, (0, 0, 0));

        SpriteFrameImage four = SpriteFrameRenderer.Render([new SpritePart(0, 0, 1, 1, 0, 0, 0)], frame, vram, 0);
        SpriteFrameImage eight = SpriteFrameRenderer.Render([new SpritePart(0, 0, 1, 1, 0, 0x100, 1)], frame, vram, 0);
        SpriteFrameImage pageOne = SpriteFrameRenderer.Render([new SpritePart(256, 0, 1, 1, 0, 0, 0)], frame, vram, 0);

        Assert.Equal(0x001F, four.Words[0]);
        Assert.Equal(0x03E0, eight.Words[0]);
        Assert.NotEqual(four.Words[0], pageOne.Words[0]);
        Assert.Equal(-1, four.Abr);
    }

    [Fact]
    public void RejectsAPartOnAnUnloadedVramPage()
    {
        FormatException error = Assert.Throws<FormatException>(() =>
            SpriteFrameRenderer.Render([new SpritePart(768, 0, 1, 1, 0, 0, 0)], Frame(0, (0, 0, 0)), SelfTestVram(), 0));
        Assert.Equal("4-bit sprite references unavailable VRAM page", error.Message);
    }

    [Fact]
    public void RejectsEightBitPartsWithoutTheirAlternateClutRow()
    {
        FormatException error = Assert.Throws<FormatException>(() =>
            SpriteFrameRenderer.Render([new SpritePart(0, 0, 1, 1, 0, 0x100, 1)], Frame(0, (0, 0, 0)), SelfTestVram(), 1));
        Assert.Equal("8-bit sprite bounds or alternate CLUT invalid", error.Message);
    }

    [Fact]
    public void RejectsInvalidPartCountsMissingPartsAndUnknownModes()
    {
        ObjectVram vram = SelfTestVram();
        Assert.Equal("sprite frame part count invalid",
            Assert.Throws<FormatException>(() => SpriteFrameRenderer.Render([], Frame(0), vram, 0)).Message);
        Assert.Equal("sprite frame references missing part",
            Assert.Throws<FormatException>(() => SpriteFrameRenderer.Render([], Frame(0, (0, 0, 0)), vram, 0)).Message);
        Assert.Equal("unsupported sprite texture mode",
            Assert.Throws<FormatException>(() => SpriteFrameRenderer.Render([new SpritePart(0, 0, 1, 1, 0, 0, 2)], Frame(0, (0, 0, 0)), vram, 0)).Message);
        Assert.Equal("sprite part bounds invalid",
            Assert.Throws<FormatException>(() => SpriteFrameRenderer.Render([new SpritePart(0, 255, 1, 2, 0, 0, 0)], Frame(0, (0, 0, 0)), vram, 0)).Message);
    }

    [Fact]
    public void ShiftsThePaletteRowByFrameRowAndClutRow()
    {
        ObjectVram vram = SelfTestVram();
        vram.Pages[0][64 * 3 + 1] = 0x7C00; // row 3, palette slot 1
        byte[] frame = Frame(abr: 0, frameRow: 1, (0, 0, 0));

        SpriteFrameImage image = SpriteFrameRenderer.Render([new SpritePart(0, 0, 1, 1, 0, 0, 0)], frame, vram, 2);

        Assert.Equal(0x7C00, image.Words[0]);
    }

    [Fact]
    public void ComposesPartsInReverseOrderSoTheFirstPartIsOnTopAndTransparentTexelsAreSkipped()
    {
        ObjectVram vram = SelfTestVram();
        // Page 0 row 1: texel 0 → index 1 (red), texel 1 → index 2 (palette word 0, transparent).
        vram.Pages[0][64] = 0x0021;
        // Page 2 row 0: texel 0 → index 3.
        vram.Pages[2][0] = 0x0003;
        vram.Pages[0][3] = 0x8005; // palette slot 3, STP set
        SpritePart top = new(0, 1, 2, 1, 0, 0, 0);
        SpritePart bottom = new(512, 0, 1, 1, 0, 0, 0);
        byte[] frame = Frame(abr: 2, (0, 4, 6), (1, 5, 6), (1, 3, 5));

        SpriteFrameImage image = SpriteFrameRenderer.Render([top, bottom], frame, vram, 0);

        Assert.Equal((3, 5, 3, 2), (image.OffsetX, image.OffsetY, image.Width, image.Height));
        // The top part (drawn last) covers (4,6) with red; its transparent texel leaves (5,6) to the bottom part.
        ushort[] expected =
        [
            0x8005, 0, 0,
            0, 0x001F, 0x8005,
        ];
        Assert.Equal(expected, image.Words);

        byte[] opaque = image.ToOpaqueRgba();
        byte[] stp = image.ToStpRgba();
        Assert.Equal(new byte[] { 255, 0, 0, 255 }, opaque[(4 * 4)..(5 * 4)]);
        Assert.Equal(new byte[] { 0, 0, 0, 0 }, stp[(4 * 4)..(5 * 4)]);
        Assert.Equal(new byte[] { 41, 0, 0, 255 }, stp[(5 * 4)..(6 * 4)]);
        Assert.Equal(new byte[] { 0, 0, 0, 0 }, opaque[(5 * 4)..(6 * 4)]);
        Assert.Equal(new byte[] { 0, 0, 0, 0 }, opaque[(1 * 4)..(2 * 4)]);
    }

    private static ObjectVram SelfTestVram()
    {
        var pages = new List<ushort[]> { new ushort[64 * 256], new ushort[64 * 256], new ushort[64 * 256] };
        pages[0][0] = 0x0001;
        pages[0][1] = 0x001F;
        pages[1][0] = 0x0002;
        var alternate = new ushort[256];
        alternate[1] = 0x03E0;
        return new ObjectVram(pages, [alternate]);
    }

    private static byte[] Frame(short abr, params (short Part, short X, short Y)[] parts) => Frame(abr, 0, parts);

    private static byte[] Frame(short abr, short frameRow, params (short Part, short X, short Y)[] parts)
    {
        byte[] frame = new byte[6 + 6 * parts.Length];
        BinaryPrimitives.WriteInt16LittleEndian(frame, (short)parts.Length);
        BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(2), frameRow);
        BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(4), abr);
        for (int index = 0; index < parts.Length; index++)
        {
            BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(6 + 6 * index), parts[index].Part);
            BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(8 + 6 * index), parts[index].X);
            BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(10 + 6 * index), parts[index].Y);
        }
        return frame;
    }
}
