using System.Buffers.Binary;
using Dw3.Formats.Binary;
using Dw3.Formats.Binary.Tim;

namespace Dw3.Formats.Field;

/// <summary>
/// Decodes the terrain packet file stored for one field stage.
/// The file starts with a 16-byte little-endian header followed by sector-aligned tile packets.
/// Each present packet contains an optional RLEN stream, one indexed TIM texture, and three ordered
/// sprite lists. Sprites in each list are composited back-to-front into one 128 by 128 layer.
/// </summary>
public static class ZonePacketDecoder
{
    private const int SectorSize = 2048;
    private const int TileSize = 128;

    /// <summary>Decodes a stage terrain packet file into three RGBA layer images and a composite.</summary>
    public static ZoneTerrain Decode(ReadOnlySpan<byte> packetFile)
    {
        if (packetFile.Length < SectorSize)
            throw new FormatException("Field packet file is shorter than one sector.");

        int presentTiles = ReadPositiveInt32(packetFile, 0, "present tile count");
        int columns = ReadPositiveInt32(packetFile, 4, "tile columns");
        int rows = ReadPositiveInt32(packetFile, 8, "tile rows");
        int packetBytes = ReadPositiveInt32(packetFile, 12, "tile packet size");
        if (packetBytes % SectorSize != 0)
            throw new FormatException("Field tile packet size is not sector-aligned.");

        int tileCount = checked(columns * rows);
        if (presentTiles > tileCount)
            throw new FormatException("Field packet header has more present tiles than grid cells.");
        if (packetFile.Length != checked(SectorSize + tileCount * packetBytes))
            throw new FormatException("Field packet file length does not match its header.");
        if (16 + checked(tileCount * 2) > SectorSize)
            throw new FormatException("Field packet tile-size table exceeds the header sector.");

        var sizes = new ushort[tileCount];
        var layers = Enumerable.Range(0, 3).Select(_ => new RgbaCanvas(checked(columns * TileSize), checked(rows * TileSize))).ToArray();
        var composite = new RgbaCanvas(checked(columns * TileSize), checked(rows * TileSize));
        var tileRecords = new List<ZoneTileRecord>();
        int actualPresent = 0;
        for (int tile = 0; tile < tileCount; tile++)
        {
            sizes[tile] = BinaryPrimitives.ReadUInt16LittleEndian(packetFile.Slice(16 + tile * 2, 2));
            if (sizes[tile] == 0)
            {
                tileRecords.Add(new ZoneTileRecord(tile, Present: false));
                continue;
            }

            actualPresent++;
            ReadOnlySpan<byte> rawPacket = packetFile.Slice(SectorSize + tile * packetBytes, packetBytes);
            var (decodedImages, record) = DecodeTileWithMetadata(rawPacket, tile);
            tileRecords.Add(record);
            foreach (var (depth, image) in decodedImages)
            {
                int x = (tile % columns) * TileSize;
                int y = (tile / columns) * TileSize;
                layers[depth].Composite(image, x, y);
                composite.Composite(image, x, y);
            }
        }
        if (actualPresent != presentTiles)
            throw new FormatException("Field packet tile-size table does not match the declared present tile count.");

        return new ZoneTerrain(columns, rows, layers.Select(layer => layer.Pixels).ToArray(), composite.Pixels, tileRecords);
    }

    private static (IReadOnlyList<(int Depth, RgbaImage Image)>, ZoneTileRecord) DecodeTileWithMetadata(ReadOnlySpan<byte> rawPacket, int tileIndex)
    {
        var (packet, wasCompressed) = RlenDecoder.DecompressIfNeeded(rawPacket);
        if (packet.Length < 16)
            throw new FormatException("Field tile packet is truncated.");
        int timOffset = ReadOffset(packet, 0, "TIM offset");
        TimImage texture = TimDecoder.Decode(packet.AsSpan(timOffset));
        if (texture.Mode != TimPixelMode.Indexed8Bit)
            throw new FormatException("Field tile texture must be an 8-bit indexed TIM.");

        int cursor = 4;
        var result = new List<(int Depth, RgbaImage Image)>();
        var sprites = new List<ZoneSpriteRecord>();
        for (int depth = 0; depth < 3; depth++)
        {
            if (cursor + 4 > packet.Length)
                throw new FormatException("Field tile sprite count is truncated.");
            int count = ReadNonNegativeInt32(packet, cursor, "sprite count");
            cursor += 4;
            if (cursor + checked(count * 12) > packet.Length)
                throw new FormatException("Field tile sprite records exceed packet bounds.");

            var layer = new RgbaCanvas(TileSize, TileSize);
            for (int index = 0; index < count; index++)
            {
                int offset = cursor + index * 12;
                short x = BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(offset, 2));
                short y = BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(offset + 2, 2));
                short u = BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(offset + 4, 2));
                short v = BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(offset + 6, 2));
                short width = BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(offset + 8, 2));
                short height = BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(offset + 10, 2));
                if (x < 0 || y < 0 || u < 0 || v < 0 || width <= 0 || height <= 0 || u + width > texture.Width || v + height > texture.Height)
                    throw new FormatException("Field tile has an invalid sprite rectangle.");
                layer.Composite(new RgbaImage(width, height, Crop(texture, u, v, width, height)), x, y);
                sprites.Add(new ZoneSpriteRecord(depth, x, y, u, v, width, height));
            }
            cursor += checked(count * 12);
            result.Add((depth, new RgbaImage(TileSize, TileSize, layer.Pixels)));
        }

        var record = new ZoneTileRecord(tileIndex, Present: true, Rlen: wasCompressed, DecodedBytes: packet.Length, TimOffset: timOffset, TimWidth: texture.Width, TimHeight: texture.Height, Sprites: sprites);
        return (result, record);
    }

    private static IReadOnlyList<(int Depth, RgbaImage Image)> DecodeTile(ReadOnlySpan<byte> rawPacket)
    {
        var (packet, _) = RlenDecoder.DecompressIfNeeded(rawPacket);
        if (packet.Length < 16)
            throw new FormatException("Field tile packet is truncated.");
        int timOffset = ReadOffset(packet, 0, "TIM offset");
        TimImage texture = TimDecoder.Decode(packet.AsSpan(timOffset));
        if (texture.Mode != TimPixelMode.Indexed8Bit)
            throw new FormatException("Field tile texture must be an 8-bit indexed TIM.");

        int cursor = 4;
        var result = new List<(int Depth, RgbaImage Image)>();
        for (int depth = 0; depth < 3; depth++)
        {
            if (cursor + 4 > packet.Length)
                throw new FormatException("Field tile sprite count is truncated.");
            int count = ReadNonNegativeInt32(packet, cursor, "sprite count");
            cursor += 4;
            if (cursor + checked(count * 12) > packet.Length)
                throw new FormatException("Field tile sprite records exceed packet bounds.");

            var layer = new RgbaCanvas(TileSize, TileSize);
            for (int index = 0; index < count; index++)
            {
                int offset = cursor + index * 12;
                short x = BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(offset, 2));
                short y = BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(offset + 2, 2));
                short u = BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(offset + 4, 2));
                short v = BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(offset + 6, 2));
                short width = BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(offset + 8, 2));
                short height = BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(offset + 10, 2));
                if (x < 0 || y < 0 || u < 0 || v < 0 || width <= 0 || height <= 0 || u + width > texture.Width || v + height > texture.Height)
                    throw new FormatException("Field tile has an invalid sprite rectangle.");
                layer.Composite(new RgbaImage(width, height, Crop(texture, u, v, width, height)), x, y);
            }
            cursor += checked(count * 12);
            result.Add((depth, new RgbaImage(TileSize, TileSize, layer.Pixels)));
        }
        return result;
    }

    private static byte[] Crop(TimImage texture, int x, int y, int width, int height)
    {
        var pixels = new byte[checked(width * height * 4)];
        for (int row = 0; row < height; row++)
            texture.Pixels.AsSpan(((y + row) * texture.Width + x) * 4, width * 4).CopyTo(pixels.AsSpan(row * width * 4));
        return pixels;
    }

    private static int ReadOffset(byte[] data, int offset, string field)
    {
        int value = ReadNonNegativeInt32(data, offset, field);
        if (value >= data.Length)
            throw new FormatException($"Field tile {field} is outside packet bounds.");
        return value;
    }

    private static int ReadPositiveInt32(ReadOnlySpan<byte> data, int offset, string field)
    {
        int value = ReadNonNegativeInt32(data, offset, field);
        if (value == 0)
            throw new FormatException($"Field packet {field} must be positive.");
        return value;
    }

    private static int ReadNonNegativeInt32(ReadOnlySpan<byte> data, int offset, string field)
    {
        if (offset < 0 || offset + 4 > data.Length)
            throw new FormatException($"Field packet {field} is truncated.");
        int value = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, 4));
        if (value < 0)
            throw new FormatException($"Field packet {field} is negative.");
        return value;
    }
}

/// <summary>Decoded terrain pixel planes for one zone.</summary>
public sealed record ZoneTerrain(int TileColumns, int TileRows, IReadOnlyList<byte[]> Layers, byte[] Composite, IReadOnlyList<ZoneTileRecord> TileRecords)
{
    /// <summary>Gets the terrain width in pixels.</summary>
    public int Width => checked(TileColumns * 128);
    /// <summary>Gets the terrain height in pixels.</summary>
    public int Height => checked(TileRows * 128);
}

/// <summary>Metadata for one tile in the zone terrain packet.</summary>
/// <param name="Tile">Zero-based tile index within the tile grid.</param>
/// <param name="Present">True when the tile has pixel data; false for absent (empty) tiles.</param>
/// <param name="Rlen">True when the raw packet was RLEN-compressed.</param>
/// <param name="DecodedBytes">Byte length of the decoded (decompressed) packet, or 0 for absent tiles.</param>
/// <param name="TimOffset">Byte offset of the TIM texture within the decoded packet, or 0 for absent tiles.</param>
/// <param name="TimWidth">TIM texture pixel width, or 0 for absent tiles.</param>
/// <param name="TimHeight">TIM texture pixel height, or 0 for absent tiles.</param>
/// <param name="Sprites">Ordered sprite records across all depth layers, or null for absent tiles.</param>
public sealed record ZoneTileRecord(int Tile, bool Present, bool Rlen = false, int DecodedBytes = 0, int TimOffset = 0, int TimWidth = 0, int TimHeight = 0, IReadOnlyList<ZoneSpriteRecord>? Sprites = null);

/// <summary>One sprite (sub-texture placement) within a tile's depth layer.</summary>
/// <param name="Depth">Depth layer index (0 = back, 1 = mid, 2 = front).</param>
/// <param name="X">Destination x within the 128×128 tile canvas.</param>
/// <param name="Y">Destination y within the 128×128 tile canvas.</param>
/// <param name="U">Source u within the TIM texture.</param>
/// <param name="V">Source v within the TIM texture.</param>
/// <param name="W">Sprite width in pixels.</param>
/// <param name="H">Sprite height in pixels.</param>
public sealed record ZoneSpriteRecord(int Depth, int X, int Y, int U, int V, int W, int H);

/// <summary>An RGBA image used only inside the field decoder.</summary>
public sealed record RgbaImage(int Width, int Height, byte[] Pixels);

internal sealed class RgbaCanvas
{
    public RgbaCanvas(int width, int height)
    {
        Width = width;
        Height = height;
        Pixels = new byte[checked(width * height * 4)];
    }

    public int Width { get; }
    public int Height { get; }
    public byte[] Pixels { get; }

    public void Composite(RgbaImage source, int x, int y)
    {
        if (x < 0 || y < 0)
            throw new FormatException("Field sprite has a negative destination coordinate.");
        int clippedWidth = Math.Min(source.Width, Width - x);
        int clippedHeight = Math.Min(source.Height, Height - y);
        if (clippedWidth <= 0 || clippedHeight <= 0)
            throw new FormatException("Field sprite does not intersect its destination layer.");
        for (int row = 0; row < clippedHeight; row++)
        {
            for (int column = 0; column < clippedWidth; column++)
            {
                int sourceOffset = (row * source.Width + column) * 4;
                byte alpha = source.Pixels[sourceOffset + 3];
                if (alpha == 0)
                    continue;
                int targetOffset = ((y + row) * Width + x + column) * 4;
                source.Pixels.AsSpan(sourceOffset, 4).CopyTo(Pixels.AsSpan(targetOffset, 4));
            }
        }
    }
}
