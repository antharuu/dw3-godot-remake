using System.IO.Compression;

namespace Dw3.Formats.Binary.Png;

/// <summary>
/// Encodes raw RGBA pixel data as a PNG file.
///
/// Produces a valid PNG with:
/// <list type="bullet">
///   <item>Colour type 6 (RGBA, 8 bits per channel).</item>
///   <item>Filter method 0, filter type 0 (None) on every row — no adaptive filtering.</item>
///   <item>IDAT data compressed with <see cref="ZLibStream"/> (RFC 1950 / zlib format),
///         which is exactly what PNG IDAT requires.</item>
///   <item>CRC-32 computed over each chunk's type and data fields (IEEE polynomial).</item>
/// </list>
///
/// This encoder intentionally does NOT produce gAMA, sRGB, pHYs, or other
/// metadata chunks: only IHDR, IDAT, IEND.  Pixel values are passed through
/// verbatim without colour-space conversion.
/// </summary>
public static class PngEncoder
{
    private static ReadOnlySpan<byte> Signature => [137, 80, 78, 71, 13, 10, 26, 10];

    // Pre-computed CRC-32 table (IEEE polynomial 0xEDB88320).
    private static readonly uint[] Crc32Table = BuildCrc32Table();

    /// <summary>
    /// Encodes RGBA pixels as PNG bytes.
    /// </summary>
    /// <param name="width">Image width in pixels (must be &gt; 0).</param>
    /// <param name="height">Image height in pixels (must be &gt; 0).</param>
    /// <param name="rgbaPixels">
    /// Raw RGBA data: 4 bytes per pixel (R, G, B, A), row-major.
    /// Length must equal <c>width * height * 4</c>.
    /// </param>
    /// <returns>A complete PNG file as a byte array.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Invalid dimensions.</exception>
    /// <exception cref="ArgumentException">Pixel buffer length mismatch.</exception>
    public static byte[] Encode(int width, int height, ReadOnlySpan<byte> rgbaPixels)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width), "Width must be positive");
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height), "Height must be positive");

        int expected = checked(width * height * 4);
        if (rgbaPixels.Length != expected)
            throw new ArgumentException(
                $"rgbaPixels length {rgbaPixels.Length} does not match {width}x{height}*4={expected}");

        using var png = new MemoryStream(expected + 256);
        png.Write(Signature);
        WriteIhdr(png, width, height);
        WriteIdat(png, width, height, rgbaPixels);
        WriteIend(png);
        return png.ToArray();
    }

    // ── IHDR ───────────────────────────────────────────────────────────────

    private static void WriteIhdr(Stream out_, int width, int height)
    {
        // IHDR is exactly 13 bytes
        Span<byte> ihdr = stackalloc byte[13];
        WriteU32Be(ihdr, 0, (uint)width);
        WriteU32Be(ihdr, 4, (uint)height);
        ihdr[8] = 8;   // bit depth
        ihdr[9] = 6;   // colour type: RGBA
        ihdr[10] = 0;  // compression method: deflate
        ihdr[11] = 0;  // filter method: adaptive (type selected per row)
        ihdr[12] = 0;  // interlace method: none
        WriteChunk(out_, "IHDR"u8, ihdr);
    }

    // ── IDAT ───────────────────────────────────────────────────────────────

    private static void WriteIdat(Stream out_, int width, int height, ReadOnlySpan<byte> rgba)
    {
        // Build filtered image data: prepend filter byte 0 (None) to each row
        int stride = width * 4;
        using var filtered = new MemoryStream(checked((stride + 1) * height));
        for (int y = 0; y < height; y++)
        {
            filtered.WriteByte(0); // filter type None
            filtered.Write(rgba.Slice(y * stride, stride));
        }
        filtered.Position = 0;

        // Compress filtered data with ZLibStream (zlib = RFC 1950, same as PNG IDAT)
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            filtered.CopyTo(zlib);
        }
        WriteChunk(out_, "IDAT"u8, compressed.GetBuffer().AsSpan(0, (int)compressed.Length));
    }

    // ── IEND ───────────────────────────────────────────────────────────────

    private static void WriteIend(Stream out_)
    {
        WriteChunk(out_, "IEND"u8, []);
    }

    // ── Chunk helper ───────────────────────────────────────────────────────

    private static void WriteChunk(Stream out_, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        // Length (4 bytes, big-endian) — counts data only, not type or CRC
        Span<byte> lenBuf = stackalloc byte[4];
        WriteU32Be(lenBuf, 0, (uint)data.Length);
        out_.Write(lenBuf);

        // Type (4 ASCII bytes)
        out_.Write(type);

        // Data
        if (!data.IsEmpty)
        {
            out_.Write(data);
        }

        // CRC-32 of type + data
        uint crc = ComputeCrc(type, data);
        Span<byte> crcBuf = stackalloc byte[4];
        WriteU32Be(crcBuf, 0, crc);
        out_.Write(crcBuf);
    }

    // ── CRC-32 (IEEE polynomial) ───────────────────────────────────────────

    private static uint ComputeCrc(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in type)
            crc = Crc32Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        foreach (byte b in data)
            crc = Crc32Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc ^ 0xFFFFFFFF;
    }

    private static uint[] BuildCrc32Table()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint c = i;
            for (int k = 0; k < 8; k++)
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            table[i] = c;
        }
        return table;
    }

    // ── Big-endian uint32 write ────────────────────────────────────────────

    private static void WriteU32Be(Span<byte> dest, int offset, uint value)
    {
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(dest[offset..], value);
    }
}
