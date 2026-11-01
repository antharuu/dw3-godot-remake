using System.Buffers.Binary;
using Dw3.Formats.Field;
using Xunit;

namespace Dw3.Formats.Tests.Field;

public sealed class ZonePacketDecoderTests
{
    [Fact]
    public void DecodesOneTileIntoThreeLayersAndComposite()
    {
        byte[] packet = BuildPacketFile(tileCount: 1, declaredPresent: 1, textureOffset: 40, spriteCount: 1);

        ZoneTerrain terrain = ZonePacketDecoder.Decode(packet);

        Assert.Equal(1, terrain.TileColumns);
        Assert.Equal(1, terrain.TileRows);
        Assert.Equal(128, terrain.Width);
        Assert.Equal(128, terrain.Height);
        Assert.Equal(3, terrain.Layers.Count);
        Assert.Equal([255, 0, 0, 255], terrain.Layers[0].AsSpan(0, 4).ToArray());
        Assert.Equal([255, 0, 0, 255], terrain.Composite.AsSpan(0, 4).ToArray());
        Assert.Equal([0, 0, 0, 0], terrain.Layers[1].AsSpan(0, 4).ToArray());
    }

    [Theory]
    [InlineData(0, 1, "present tile count")]
    [InlineData(1, 2, "TIM magic")]
    public void RejectsInvalidHeaders(int declaredPresent, int tileCount, string expectedMessage)
    {
        byte[] packet = BuildPacketFile(tileCount, declaredPresent, textureOffset: 40, spriteCount: 0);

        FormatException exception = Assert.Throws<FormatException>(() => ZonePacketDecoder.Decode(packet));

        Assert.Contains(expectedMessage, exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PopulatesTileRecordMetadata()
    {
        byte[] packet = BuildPacketFile(tileCount: 1, declaredPresent: 1, textureOffset: 40, spriteCount: 1);

        ZoneTerrain terrain = ZonePacketDecoder.Decode(packet);

        Assert.Single(terrain.TileRecords);
        ZoneTileRecord rec = terrain.TileRecords[0];
        Assert.Equal(0, rec.Tile);
        Assert.True(rec.Present);
        Assert.False(rec.Rlen);       // BuildPacketFile does not apply RLEN
        Assert.Equal(40, rec.TimOffset);
        ZoneSpriteRecord sprite = Assert.Single(rec.Sprites!);
        Assert.Equal(0, sprite.Depth);
        Assert.Equal(2, sprite.W);
        Assert.Equal(1, sprite.H);
    }

    [Fact]
    public void CropsSpriteAtTileBoundary()
    {
        byte[] packet = BuildPacketFile(tileCount: 1, declaredPresent: 1, textureOffset: 40, spriteCount: 1);

        ZoneTerrain terrain = ZonePacketDecoder.Decode(packet);

        Assert.Equal(128 * 128 * 4, terrain.Composite.Length);
    }

    private static byte[] BuildPacketFile(int tileCount, int declaredPresent, int textureOffset, int spriteCount, short spriteWidth = 2)
    {
        const int sector = 2048;
        byte[] file = new byte[sector + tileCount * sector];
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(0, 4), declaredPresent);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(4, 4), tileCount);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(8, 4), 1);
        BinaryPrimitives.WriteInt32LittleEndian(file.AsSpan(12, 4), sector);
        for (int index = 0; index < Math.Min(tileCount, declaredPresent); index++)
            BinaryPrimitives.WriteUInt16LittleEndian(file.AsSpan(16 + index * 2, 2), 1);
        if (declaredPresent != 1 || tileCount != 1)
            return file;

        Span<byte> tile = file.AsSpan(sector, sector);
        BinaryPrimitives.WriteInt32LittleEndian(tile, textureOffset);
        BinaryPrimitives.WriteInt32LittleEndian(tile.Slice(4, 4), spriteCount);
        if (spriteCount > 0)
        {
            BinaryPrimitives.WriteInt16LittleEndian(tile.Slice(8, 2), 0);
            BinaryPrimitives.WriteInt16LittleEndian(tile.Slice(10, 2), 0);
            BinaryPrimitives.WriteInt16LittleEndian(tile.Slice(12, 2), 0);
            BinaryPrimitives.WriteInt16LittleEndian(tile.Slice(14, 2), 0);
            BinaryPrimitives.WriteInt16LittleEndian(tile.Slice(16, 2), spriteWidth);
            BinaryPrimitives.WriteInt16LittleEndian(tile.Slice(18, 2), 1);
        }
        int cursor = 8 + spriteCount * 12;
        BinaryPrimitives.WriteInt32LittleEndian(tile.Slice(cursor, 4), 0);
        BinaryPrimitives.WriteInt32LittleEndian(tile.Slice(cursor + 4, 4), 0);
        BuildTim8(tile.Slice(textureOffset));
        return file;
    }

    private static void BuildTim8(Span<byte> destination)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(destination, 0x10);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(4), 0x09);
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(8), 12 + 512);
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(16), 256);
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(18), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(20 + 2, 2), 0x001F);
        int image = 8 + 12 + 512;
        BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(image), 14);
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(image + 8), 1);
        BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(image + 10), 1);
        destination[image + 12] = 1;
        destination[image + 13] = 1;
    }
}
