using System.Buffers.Binary;
using Dw3.Formats.Water;
using Xunit;

namespace Dw3.Formats.Tests.Water;

public sealed class StageObjectDecoderTests
{
    private const int TableOffset = 0x40;

    [Fact]
    public void RendersEveryCycleStepOfVisibleSheetBackedObjects()
    {
        byte[] overlay = Overlay(
            (1, 0, 1, 1, 2, 0),    // frame cycle 1..2
            (1, 0xFF, 0, 0, 0, 0), // shared hidden-spot sprite: not drawn here
            (0, 0, 0, 0, 0, 0),    // hidden
            (1, 0, 2, 0, 1, 1));   // palette cycle rows 0..1 on frame 1

        StageObjectSet set = StageObjectDecoder.Decode(123, overlay, Tmpk(), Mask());

        Assert.Equal(1, set.ArchiveEntries);
        Assert.Equal([0, 3], set.Objects.Select(item => item.Index));
        Assert.Equal([(1, 0), (2, 0)], set.Objects[0].Frames.Select(frame => (frame.FrameId, frame.ClutRow)));
        Assert.Equal([(1, 0), (1, 1)], set.Objects[1].Frames.Select(frame => (frame.FrameId, frame.ClutRow)));
        Assert.Equal(0x1111, set.Objects[1].Frames[0].Image.Words[0]);
        Assert.Equal(0x2222, set.Objects[1].Frames[1].Image.Words[0]);
        Assert.Equal(0x800A5DE0u + TableOffset + 3 * 18, set.Objects[1].Tile.SourceAddress);
    }

    [Fact]
    public void RejectsAnObjectWhoseFrameIsAbsentFromTheSheet()
    {
        byte[] overlay = Overlay((1, 0, 1, 1, 3, 0));

        FormatException error = Assert.Throws<FormatException>(() => StageObjectDecoder.Decode(123, overlay, Tmpk(), Mask()));
        Assert.Equal("S123 object=0 frame=0x3 absent", error.Message);
    }

    [Fact]
    public void RejectsAStageWithoutDrawableObjects()
    {
        byte[] overlay = Overlay((0, 0, 0, 0, 0, 0));

        FormatException error = Assert.Throws<FormatException>(() => StageObjectDecoder.Decode(7, overlay, Tmpk(), Mask()));
        Assert.Equal("S007 has no visible source map objects", error.Message);
    }

    private static byte[] Overlay(params (byte Visible, byte Anim, byte Cycle, byte First, byte Last, byte Frame)[] tiles)
    {
        uint[] code =
        [
            0x3C02800Au,                       // lui v0, 0x800A
            0x24519D80u,                       // addiu s1, v0, -0x6280 (descriptor)
            0x3C030123u,                       // lui v1, 0x0123 (TMPK file, entry 0)
            0xAE23000Cu,                       // sw v1, 0x0C(s1)
            0x3C02800Au,                       // lui v0, 0x800A
            0x24420000u | (0x5DE0 + TableOffset), // addiu v0, v0, table
            0xAE220010u,                       // sw v0, 0x10(s1)
            0x03E00008u, 0,                    // jr ra ; nop
        ];
        byte[] overlay = new byte[(TableOffset + 18 * (tiles.Length + 1) + 3) / 4 * 4];
        for (int index = 0; index < code.Length; index++)
            BinaryPrimitives.WriteUInt32LittleEndian(overlay.AsSpan(index * 4), code[index]);
        for (int index = 0; index < tiles.Length; index++)
        {
            var tile = tiles[index];
            StageTileDecoderTests.WriteTile(overlay, TableOffset + 18 * index, tile.Visible, tile.Anim, 1, 0, tile.Frame,
                tile.Cycle, tile.First, tile.Last, 0, 0, 0, 0, 0, 0);
        }
        return overlay;
    }

    private static byte[] Tmpk()
    {
        byte[] frame = new byte[12];
        BinaryPrimitives.WriteInt16LittleEndian(frame, 1);
        byte[] sheet = SpriteSheetDecoderTests.BuildSheet(parts: [[0, 0, 1, 1, 0, 0, 0]], frameIds: [1, 2], frames: [frame, frame]);
        return [.. SpriteSheetDecoderTests.U32(8), .. SpriteSheetDecoderTests.U32(8 + sheet.Length), .. sheet];
    }

    private static byte[] Mask()
    {
        ushort[] page = new ushort[64 * 256];
        page[0] = 0x0001;              // texel (0,0) → palette index 1
        page[1] = 0x1111;              // palette row 0, index 1
        page[64 + 1] = 0x2222;         // palette row 1, index 1
        return ObjectTextureArchiveDecoderTests.BuildArchive(ObjectTextureArchiveDecoderTests.Tim(null, 0, page));
    }
}
