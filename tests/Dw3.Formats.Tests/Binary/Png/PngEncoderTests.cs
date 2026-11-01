using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using Dw3.Formats.Binary.Png;
using Xunit;

namespace Dw3.Formats.Tests.Binary.Png;

public sealed class PngEncoderTests
{
    private static readonly byte[] PngSignature = [137, 80, 78, 71, 13, 10, 26, 10];

    // ── PNG signature ─────────────────────────────────────────────────────

    [Fact]
    public void Encode_ProducesValidPngSignature()
    {
        var png = PngEncoder.Encode(1, 1, [0, 0, 0, 255]);
        Assert.True(png.Length >= 8);
        Assert.Equal(PngSignature, png[..8]);
    }

    // ── IHDR chunk ────────────────────────────────────────────────────────

    [Fact]
    public void Encode_1x1_IhdrHasCorrectDimensions()
    {
        var png = PngEncoder.Encode(1, 1, [0xFF, 0x00, 0x00, 0xFF]);
        var ihdr = ReadChunk(png, 8);
        Assert.Equal("IHDR", ihdr.Type);
        Assert.Equal(13, ihdr.Data.Length);
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32BigEndian(ihdr.Data[..4]));   // width
        Assert.Equal(1u, BinaryPrimitives.ReadUInt32BigEndian(ihdr.Data[4..8]));  // height
        Assert.Equal(8, ihdr.Data[8]);    // bit depth
        Assert.Equal(6, ihdr.Data[9]);    // colour type: RGBA
        Assert.Equal(0, ihdr.Data[10]);   // compression method
        Assert.Equal(0, ihdr.Data[11]);   // filter method
        Assert.Equal(0, ihdr.Data[12]);   // interlace method
    }

    [Fact]
    public void Encode_16x32_IhdrDimensions()
    {
        var pixels = new byte[16 * 32 * 4];
        var png = PngEncoder.Encode(16, 32, pixels);
        var ihdr = ReadChunk(png, 8);
        Assert.Equal(16u, BinaryPrimitives.ReadUInt32BigEndian(ihdr.Data[..4]));
        Assert.Equal(32u, BinaryPrimitives.ReadUInt32BigEndian(ihdr.Data[4..8]));
    }

    // ── Round-trip RGBA via IDAT decompression ────────────────────────────

    [Fact]
    public void Encode_RoundTrip_PreservesPixels()
    {
        // 2×2 image with known pixel values
        byte[] rgba = [
            0xFF, 0x00, 0x00, 0xFF,   // pixel 0,0: red  opaque
            0x00, 0xFF, 0x00, 0xFF,   // pixel 1,0: green opaque
            0x00, 0x00, 0xFF, 0xFF,   // pixel 0,1: blue opaque
            0x00, 0x00, 0x00, 0x00 // pixel 1,1: transparent
        ];
        var png = PngEncoder.Encode(2, 2, rgba);
        var decoded = DecodePngToRgba(png, 2, 2);
        Assert.Equal(rgba, decoded);
    }

    [Fact]
    public void Encode_AllTransparent_RoundTrip()
    {
        var rgba = new byte[4 * 4 * 4]; // 4×4 all zeroes = transparent
        var png = PngEncoder.Encode(4, 4, rgba);
        var decoded = DecodePngToRgba(png, 4, 4);
        Assert.Equal(rgba, decoded);
    }

    [Fact]
    public void Encode_SingleWhitePixel_RoundTrip()
    {
        byte[] rgba = [0xFF, 0xFF, 0xFF, 0xFF];
        var png = PngEncoder.Encode(1, 1, rgba);
        var decoded = DecodePngToRgba(png, 1, 1);
        Assert.Equal(rgba, decoded);
    }

    // ── Error cases ───────────────────────────────────────────────────────

    [Fact]
    public void Encode_ZeroWidth_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PngEncoder.Encode(0, 1, [0, 0, 0, 255]));
    }

    [Fact]
    public void Encode_ZeroHeight_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PngEncoder.Encode(1, 0, [0, 0, 0, 255]));
    }

    [Fact]
    public void Encode_WrongPixelBufferLength_ThrowsArgumentException()
    {
        // 2×1 needs 8 bytes, we give 4
        var ex = Assert.Throws<ArgumentException>(() => PngEncoder.Encode(2, 1, [0, 0, 0, 255]));
        Assert.Contains("length", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── IEND presence ─────────────────────────────────────────────────────

    [Fact]
    public void Encode_EndsWithIendChunk()
    {
        var png = PngEncoder.Encode(1, 1, [0, 0, 0, 255]);
        // IEND chunk = 4 (len=0) + 4 (type) + 0 (data) + 4 (CRC) = 12 bytes at end
        Assert.True(png.Length >= 12);
        int iendStart = png.Length - 12;
        string type = Encoding.ASCII.GetString(png, iendStart + 4, 4);
        Assert.Equal("IEND", type);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static (string Type, byte[] Data) ReadChunk(byte[] png, int offset)
    {
        uint length = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset, 4));
        string type = Encoding.ASCII.GetString(png, offset + 4, 4);
        byte[] data = png[(offset + 8)..(offset + 8 + (int)length)];
        return (type, data);
    }

    /// <summary>
    /// Minimal PNG decoder that reads an RGBA (colour type 6) PNG and returns
    /// raw RGBA bytes, handling only filter type 0 (None).
    /// Reuses the same ZLibStream approach as the production parity tool.
    /// </summary>
    private static byte[] DecodePngToRgba(byte[] png, int width, int height)
    {
        int offset = 8; // skip signature
        using var idat = new MemoryStream();

        while (offset < png.Length)
        {
            uint length = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(offset, 4));
            string type = Encoding.ASCII.GetString(png, offset + 4, 4);
            if (type == "IDAT")
                idat.Write(png, offset + 8, (int)length);
            if (type == "IEND")
                break;
            offset += 12 + (int)length;
        }

        int stride = width * 4;
        var raw = new byte[(stride + 1) * height];
        idat.Position = 0;
        using (var zlib = new ZLibStream(idat, CompressionMode.Decompress))
        {
            zlib.ReadExactly(raw);
        }

        var result = new byte[stride * height];
        for (int y = 0; y < height; y++)
        {
            int filterType = raw[y * (stride + 1)];
            Assert.Equal(0, filterType); // we only write filter type 0
            raw.AsSpan(y * (stride + 1) + 1, stride).CopyTo(result.AsSpan(y * stride, stride));
        }
        return result;
    }
}
