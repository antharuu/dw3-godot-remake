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
