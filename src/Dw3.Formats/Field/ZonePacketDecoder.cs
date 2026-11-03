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
    /// <param name="packetFile">Raw bytes of the stage terrain packet file from the disc.</param>
    /// <returns>The decoded terrain with layer images, composite image and tile metadata.</returns>
    /// <exception cref="FormatException">The packet file is shorter than one sector, or a tile or layer is malformed.</exception>
    public static ZoneTerrain Decode(ReadOnlySpan<byte> packetFile)
    {
        if (packetFile.Length < SectorSize)
        {
            throw new FormatException("Field packet file is shorter than one sector.");
        }

        int presentTiles = ReadPositiveInt32(packetFile, 0, "present tile count");
        int columns = ReadPositiveInt32(packetFile, 4, "tile columns");
        int rows = ReadPositiveInt32(packetFile, 8, "tile rows");
        int packetBytes = ReadPositiveInt32(packetFile, 12, "tile packet size");
        if (packetBytes % SectorSize != 0)
        {
            throw new FormatException("Field tile packet size is not sector-aligned.");
        }

        int tileCount = checked(columns * rows);
        if (presentTiles > tileCount)
        {
            throw new FormatException("Field packet header has more present tiles than grid cells.");
        }

        if (packetFile.Length != checked(SectorSize + (tileCount * packetBytes)))
        {
            throw new FormatException("Field packet file length does not match its header.");
        }

        if (16 + checked(tileCount * 2) > SectorSize)
        {
            throw new FormatException("Field packet tile-size table exceeds the header sector.");
        }

        var sizes = new ushort[tileCount];
        var layers = Enumerable.Range(0, 3).Select(_ => new RgbaCanvas(checked(columns * TileSize), checked(rows * TileSize))).ToArray();
        var composite = new RgbaCanvas(checked(columns * TileSize), checked(rows * TileSize));
        var tileRecords = new List<ZoneTileRecord>();
        int actualPresent = 0;
        for (int tile = 0; tile < tileCount; tile++)
        {
            sizes[tile] = BinaryPrimitives.ReadUInt16LittleEndian(packetFile.Slice(16 + (tile * 2), 2));
            if (sizes[tile] == 0)
            {
                tileRecords.Add(new ZoneTileRecord(tile, Present: false));
                continue;
            }

            actualPresent++;
            ReadOnlySpan<byte> rawPacket = packetFile.Slice(SectorSize + (tile * packetBytes), packetBytes);
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
        {
            throw new FormatException("Field packet tile-size table does not match the declared present tile count.");
        }

        return new ZoneTerrain(columns, rows, [.. layers.Select(layer => layer.Pixels)], composite.Pixels, tileRecords);
    }

    /// <summary>
    /// Decodes one raw tile packet (RLEN-decompressed if needed) into its RGBA depth layers and tile record.
    /// Each tile has up to 3 depth layers decoded from consecutive 8-bit TIM layer records within the packet.
    /// </summary>
    /// <param name="rawPacket">Raw (possibly RLEN-compressed) bytes of one tile sector.</param>
    /// <param name="tileIndex">Zero-based tile index, used in tile record and error messages.</param>
    /// <returns>Ordered list of (depth, RGBA image) pairs for this tile, and the tile's metadata record.</returns>
    /// <exception cref="FormatException">The packet is truncated, the TIM is not an 8-bit indexed image, or a layer record is malformed.</exception>
    private static (IReadOnlyList<(int Depth, RgbaImage Image)> Layers, ZoneTileRecord Record) DecodeTileWithMetadata(ReadOnlySpan<byte> rawPacket, int tileIndex)
    {
        var (packet, wasCompressed) = RlenDecoder.DecompressIfNeeded(rawPacket);
        if (packet.Length < 16)
        {
            throw new FormatException("Field tile packet is truncated.");
        }

        int timOffset = ReadOffset(packet, 0, "TIM offset");
        TimImage texture = TimDecoder.Decode(packet.AsSpan(timOffset));
        if (texture.Mode != TimPixelMode.Indexed8Bit)
        {
            throw new FormatException("Field tile texture must be an 8-bit indexed TIM.");
        }

        int cursor = 4;
        var result = new List<(int Depth, RgbaImage Image)>();
        var sprites = new List<ZoneSpriteRecord>();
        for (int depth = 0; depth < 3; depth++)
        {
            cursor = DecodeSpriteLayer(packet, texture, cursor, depth, result, sprites);
        }

        var record = new ZoneTileRecord(tileIndex, Present: true, Rlen: wasCompressed, DecodedBytes: packet.Length, TimOffset: timOffset, TimWidth: texture.Width, TimHeight: texture.Height, Sprites: sprites);
        return (result, record);
    }

    /// <summary>Decodes the sprite list at cursor for one depth layer and advances the cursor.</summary>
    /// <param name="packet">Decoded (decompressed) packet bytes.</param>
    /// <param name="texture">Indexed TIM texture used for cropping.</param>
    /// <param name="cursor">Current byte position within the packet sprite section.</param>
    /// <param name="depth">Depth layer index (0 = back, 1 = mid, 2 = front).</param>
    /// <param name="result">Accumulates (depth, image) pairs for this layer.</param>
    /// <param name="sprites">Accumulates sprite records for the tile metadata.</param>
    /// <returns>Updated cursor position after consuming all sprites for this depth.</returns>
    private static int DecodeSpriteLayer(byte[] packet, TimImage texture, int cursor, int depth, List<(int Depth, RgbaImage Image)> result, List<ZoneSpriteRecord> sprites)
    {
        if (cursor + 4 > packet.Length)
        {
            throw new FormatException("Field tile sprite count is truncated.");
        }

        int count = ReadNonNegativeInt32(packet, cursor, "sprite count");
        cursor += 4;
        if (cursor + checked(count * 12) > packet.Length)
        {
            throw new FormatException("Field tile sprite records exceed packet bounds.");
        }

        var layer = new RgbaCanvas(TileSize, TileSize);
        for (int index = 0; index < count; index++)
        {
            int offset = cursor + (index * 12);
            short x = BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(offset, 2));
            short y = BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(offset + 2, 2));
            short u = BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(offset + 4, 2));
            short v = BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(offset + 6, 2));
            short width = BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(offset + 8, 2));
            short height = BinaryPrimitives.ReadInt16LittleEndian(packet.AsSpan(offset + 10, 2));
            if (x < 0 || y < 0 || u < 0 || v < 0 || width <= 0 || height <= 0 || u + width > texture.Width || v + height > texture.Height)
            {
                throw new FormatException("Field tile has an invalid sprite rectangle.");
            }

            layer.Composite(new RgbaImage(width, height, Crop(texture, u, v, width, height)), x, y);
            sprites.Add(new ZoneSpriteRecord(depth, x, y, u, v, width, height));
        }

        cursor += checked(count * 12);
        result.Add((depth, new RgbaImage(TileSize, TileSize, layer.Pixels)));
        return cursor;
    }

    /// <summary>
    /// Crops a rectangular region from <paramref name="texture"/> and returns it as a new RGBA byte array.
    /// </summary>
    /// <param name="texture">Source TIM image to crop from.</param>
    /// <param name="x">Left pixel coordinate of the crop region.</param>
    /// <param name="y">Top pixel coordinate of the crop region.</param>
    /// <param name="width">Width of the crop region in pixels.</param>
    /// <param name="height">Height of the crop region in pixels.</param>
    /// <returns>RGBA pixel data (4 bytes per pixel, row-major) for the requested region.</returns>
    private static byte[] Crop(TimImage texture, int x, int y, int width, int height)
    {
        var pixels = new byte[checked(width * height * 4)];
        for (int row = 0; row < height; row++)
        {
            texture.Pixels.AsSpan((((y + row) * texture.Width) + x) * 4, width * 4).CopyTo(pixels.AsSpan(row * width * 4));
        }

        return pixels;
    }

    /// <summary>Reads a non-negative s32 offset from <paramref name="data"/> at <paramref name="offset"/> and validates it lies within the packet.</summary>
    /// <param name="data">Packet byte array.</param>
    /// <param name="offset">Byte offset; the read covers 4 bytes.</param>
    /// <param name="field">Field name for error messages.</param>
    /// <returns>The decoded offset value, already validated to lie within the packet.</returns>
    /// <exception cref="FormatException">The offset value is out of packet bounds.</exception>
    private static int ReadOffset(byte[] data, int offset, string field)
    {
        int value = ReadNonNegativeInt32(data, offset, field);
        if (value >= data.Length)
        {
            throw new FormatException($"Field tile {field} is outside packet bounds.");
        }

        return value;
    }

    /// <summary>Reads a strictly positive s32 from <paramref name="data"/> at <paramref name="offset"/>; throws when zero or negative.</summary>
    /// <param name="data">Source data span.</param>
    /// <param name="offset">Byte offset; the read covers 4 bytes.</param>
    /// <param name="field">Field name for error messages.</param>
    /// <returns>The decoded positive integer value.</returns>
    /// <exception cref="FormatException">The value is zero, negative, or the read overruns the span.</exception>
    private static int ReadPositiveInt32(ReadOnlySpan<byte> data, int offset, string field)
    {
        int value = ReadNonNegativeInt32(data, offset, field);
        if (value == 0)
        {
            throw new FormatException($"Field packet {field} must be positive.");
        }

        return value;
    }

    /// <summary>Reads a non-negative s32 from <paramref name="data"/> at <paramref name="offset"/>; throws when negative or the read overruns the span.</summary>
    /// <param name="data">Source data span.</param>
    /// <param name="offset">Byte offset; the read covers 4 bytes.</param>
    /// <param name="field">Field name for error messages.</param>
    /// <returns>The decoded non-negative integer value.</returns>
    /// <exception cref="FormatException">The read overruns the span or the value is negative.</exception>
    private static int ReadNonNegativeInt32(ReadOnlySpan<byte> data, int offset, string field)
    {
        if (offset < 0 || offset + 4 > data.Length)
        {
            throw new FormatException($"Field packet {field} is truncated.");
        }

        int value = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, 4));
        if (value < 0)
        {
            throw new FormatException($"Field packet {field} is negative.");
        }

        return value;
    }
}
