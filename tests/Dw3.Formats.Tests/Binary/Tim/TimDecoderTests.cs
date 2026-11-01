using System.Buffers.Binary;
using Dw3.Formats.Binary.Tim;
using Xunit;

namespace Dw3.Formats.Tests.Binary.Tim;

public sealed class TimDecoderTests
{
    // ── TIM builder helpers ───────────────────────────────────────────────

    /// <summary>
    /// Builds a minimal 4-bit (flags=0x08) or 8-bit (flags=0x09) TIM with a
    /// single CLUT row and a 1×1 image.
    /// </summary>
    private static byte[] Build4Or8BitTim(bool is4Bit, ushort[] clutEntries, byte[] pixelData,
        int widthWords, int height)
    {
        uint flags = is4Bit ? 0x08u : 0x09u;
        int numColors = is4Bit ? 16 : 256;

        // CLUT block = 12-byte header + (numColors * 2) bytes
        int clutDataBytes = numColors * 2;
        uint clutBlockLen = (uint)(12 + clutDataBytes); // includes the 4-byte length field itself

        // Image block = 12-byte header + pixelData bytes
        uint imgBlockLen = (uint)(12 + pixelData.Length);

        int totalLen = 8 + (int)clutBlockLen + (int)imgBlockLen;
        var tim = new byte[totalLen];
        int pos = 0;

        // TIM header
        WriteU32Le(tim, pos, 0x00000010u); pos += 4; // magic
        WriteU32Le(tim, pos, flags); pos += 4;        // flags

        // CLUT block
        WriteU32Le(tim, pos, clutBlockLen); pos += 4;
        WriteU16Le(tim, pos, 0); pos += 2;  // VRAM X
        WriteU16Le(tim, pos, 0); pos += 2;  // VRAM Y
        WriteU16Le(tim, pos, (ushort)numColors); pos += 2;
        WriteU16Le(tim, pos, 1); pos += 2;  // 1 row
        foreach (var w in clutEntries)
        {
            WriteU16Le(tim, pos, w); pos += 2;
        }

        // Image block
        WriteU32Le(tim, pos, imgBlockLen); pos += 4;
        WriteU16Le(tim, pos, 0); pos += 2;           // VRAM X
        WriteU16Le(tim, pos, 0); pos += 2;           // VRAM Y
        WriteU16Le(tim, pos, (ushort)widthWords); pos += 2;
        WriteU16Le(tim, pos, (ushort)height); pos += 2;
        Array.Copy(pixelData, 0, tim, pos, pixelData.Length);

        return tim;
    }

    /// <summary>Builds a 16-bit direct TIM (flags=0x02) with given pixel words.</summary>
    private static byte[] Build16BitTim(int width, int height, ushort[] pixels)
    {
        // No CLUT block; image block = 12-byte header + width*height*2 bytes
        uint imgBlockLen = (uint)(12 + width * height * 2);
        int totalLen = 8 + (int)imgBlockLen;
        var tim = new byte[totalLen];
        int pos = 0;

        WriteU32Le(tim, pos, 0x00000010u); pos += 4;
        WriteU32Le(tim, pos, 0x00000002u); pos += 4;
        WriteU32Le(tim, pos, imgBlockLen); pos += 4;
        WriteU16Le(tim, pos, 0); pos += 2;           // VRAM X
        WriteU16Le(tim, pos, 0); pos += 2;           // VRAM Y
        WriteU16Le(tim, pos, (ushort)width); pos += 2;
        WriteU16Le(tim, pos, (ushort)height); pos += 2;
        foreach (var w in pixels)
        {
            WriteU16Le(tim, pos, w); pos += 2;
        }
        return tim;
    }

    private static void WriteU32Le(byte[] arr, int offset, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(arr.AsSpan(offset, 4), value);

    private static void WriteU16Le(byte[] arr, int offset, ushort value) =>
        BinaryPrimitives.WriteUInt16LittleEndian(arr.AsSpan(offset, 2), value);

    // ── 4-bit TIM ─────────────────────────────────────────────────────────

    [Fact]
    public void Decode4Bit_2x1_CorrectPixels()
    {
        // 2×1 image = 1 byte pixel data (two nibbles)
        // CLUT: index 0 = 0x0000 (transparent), index 1 = red (R=31, G=0, B=0)
        var clut = new ushort[16];
        clut[0] = 0x0000;   // transparent
        clut[1] = 0x001F;   // R=31 → R=255, G=0, B=0, A=255
                            // pixel byte: lo nibble=1, hi nibble=0 → pixel0=clut[1], pixel1=clut[0]
        _ = Build4Or8BitTim(is4Bit: true, clut, pixelData: [0x01], widthWords: 1 /* 4px? no */, height: 1);

        // 2 pixels wide: widthWords=1, width=1*4=4 — too wide. Let's correct:
        // For a 2×1 image: widthWords=1, width=4. But pixelData[0]=0x01 gives pixel0=index1, pixel1=index0, pixel2=index0, pixel3=index0
        // We want width=4 so let's just test with widthWords=1, pixel data covering 4 pixels.
        // 4 pixels = 2 bytes: byte0=0x10 (pixel0=0, pixel1=1), byte1=0x00 (pixel2=0, pixel3=0)
        var clut2 = new ushort[16];
        clut2[0] = 0x0000;   // transparent
        clut2[1] = 0x001F;   // R=255
        var tim2 = Build4Or8BitTim(is4Bit: true, clut2,
            pixelData: [0x10, 0x00], // pixel0=idx0 (transparent), pixel1=idx1 (red), pixel2/3 transparent
            widthWords: 1, height: 1); // 4×1

        var img = TimDecoder.Decode(tim2);
        Assert.Equal(4, img.Width);
        Assert.Equal(1, img.Height);
        Assert.Equal(TimPixelMode.Indexed4Bit, img.Mode);

        // pixel 0: index 0 → transparent (0,0,0,0)
        Assert.Equal(0, img.Pixels[0]);
        Assert.Equal(0, img.Pixels[3]);  // alpha = 0

        // pixel 1: index 1 → red (255, 0, 0, 255)
        Assert.Equal(255, img.Pixels[4]);   // R
        Assert.Equal(0, img.Pixels[5]);     // G
        Assert.Equal(0, img.Pixels[6]);     // B
        Assert.Equal(255, img.Pixels[7]);   // A
    }

    [Fact]
    public void Decode4Bit_MultipleClutRows_SelectsCorrectRow()
    {
        // Build a TIM with 2 CLUT rows. Row 0 has only transparent entries; row 1 has white at index 0.
        uint flags = 0x08u;
        int numColors = 16;
        int numRows = 2;
        int clutDataBytes = numColors * numRows * 2;
        uint clutBlockLen = (uint)(12 + clutDataBytes);
        // image block: 4 pixels wide (widthWords=1), 1 high, 2 bytes pixel data, block length = 12 + 2

        var tim = new List<byte>();
        void AddU32(uint v) { var b = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(b, v); tim.AddRange(b); }
        void AddU16(ushort v) { var b = new byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(b, v); tim.AddRange(b); }

        AddU32(0x00000010u);   // magic
        AddU32(flags);          // flags
        AddU32(clutBlockLen);   // CLUT block length
        AddU16(0); AddU16(0);   // VRAM X, Y
        AddU16((ushort)numColors);
        AddU16((ushort)numRows);
        // Row 0: all transparent
        for (int i = 0; i < numColors; i++) AddU16(0x0000);
        // Row 1: index 0 = white (R=31, G=31, B=31 → word = 0x7FFF, but 0x0000 is transparent)
        // Use word 0x7FFF (bit pattern: R=31, G=31, B=31, STP=0) for white
        AddU16(0x7FFF); // index 0 row 1 = white opaque
        for (int i = 1; i < numColors; i++) AddU16(0x0000);
        // Image block: 1 pixel (index 0), 4 wide, 1 high
        AddU32(14); // block length = 12 + 2 bytes of pixel data
        AddU16(0); AddU16(0); // VRAM X, Y
        AddU16(1); // words_per_line = 1 → width = 4
        AddU16(1); // height = 1
        // pixel data: 2 bytes for 4 pixels; all index 0
        tim.AddRange([0x00, 0x00]);

        byte[] timBytes = [.. tim];

        // Row 0: index 0 → transparent
        var img0 = TimDecoder.Decode(timBytes, clutRow: 0);
        Assert.Equal(0, img0.Pixels[3]); // alpha = 0

        // Row 1: index 0 → white
        var img1 = TimDecoder.Decode(timBytes, clutRow: 1);
        Assert.Equal(255, img1.Pixels[3]); // alpha = 255
        Assert.Equal(255, img1.Pixels[0]); // R = 255 (31*255/31=255)
    }

    // ── 8-bit TIM ─────────────────────────────────────────────────────────

    [Fact]
    public void Decode8Bit_2x1_CorrectPixels()
    {
        // 2×1 image: widthWords=1, width=2, height=1 → 2 bytes pixel data
        var clut = new ushort[256];
        clut[0] = 0x0000;   // transparent
        clut[1] = 0x03E0;   // G=31 → G=255, others 0, A=255

        var tim = Build4Or8BitTim(is4Bit: false, clut,
            pixelData: [0, 1], // pixel0 = index 0, pixel1 = index 1
            widthWords: 1, height: 1); // 2×1

        var img = TimDecoder.Decode(tim);
        Assert.Equal(2, img.Width);
        Assert.Equal(1, img.Height);
        Assert.Equal(TimPixelMode.Indexed8Bit, img.Mode);

        // pixel 0: transparent
        Assert.Equal(0, img.Pixels[3]);

        // pixel 1: green (0, 255, 0, 255)
        Assert.Equal(0, img.Pixels[4]);
        Assert.Equal(255, img.Pixels[5]);
        Assert.Equal(0, img.Pixels[6]);
        Assert.Equal(255, img.Pixels[7]);
    }

    // ── 16-bit direct TIM ─────────────────────────────────────────────────

    [Fact]
    public void Decode16Bit_TransparentPixel_AlphaZero()
    {
        var tim = Build16BitTim(1, 1, pixels: [0x0000]);
        var img = TimDecoder.Decode(tim);
        Assert.Equal(1, img.Width);
        Assert.Equal(1, img.Height);
        Assert.Equal(TimPixelMode.Direct15Bit, img.Mode);
        Assert.Equal(0, img.Pixels[3]); // A = 0
    }

    [Fact]
    public void Decode16Bit_OpaqueRed_CorrectRgba()
    {
        // R=31, G=0, B=0, STP=0 → word = 0x001F
        var tim = Build16BitTim(1, 1, pixels: [0x001F]);
        var img = TimDecoder.Decode(tim);
        Assert.Equal(255, img.Pixels[0]); // R = 255
        Assert.Equal(0, img.Pixels[1]);   // G = 0
        Assert.Equal(0, img.Pixels[2]);   // B = 0
        Assert.Equal(255, img.Pixels[3]); // A = 255
    }

    [Fact]
    public void Decode16Bit_StpBitIgnored_AlphaAlways255()
    {
        // R=1, G=0, B=0, STP=1 → word = 0x8001
        var tim = Build16BitTim(1, 1, pixels: [0x8001]);
        var img = TimDecoder.Decode(tim);
        // STP bit set but colour ≠ 0 → still opaque
        Assert.Equal(255, img.Pixels[3]);
    }

    [Fact]
    public void Decode16Bit_2x2_PixelCount()
    {
        ushort[] pixels = [0x001F, 0x03E0, 0x7C00, 0x0000]; // R, G, B, transparent
        var tim = Build16BitTim(2, 2, pixels);
        var img = TimDecoder.Decode(tim);
        Assert.Equal(2, img.Width);
        Assert.Equal(2, img.Height);
        Assert.Equal(16, img.Pixels.Length); // 4 pixels × 4 channels
    }

    // ── Error cases ───────────────────────────────────────────────────────

    [Fact]
    public void Decode_WrongMagic_ThrowsFormatException()
    {
        byte[] bad = [0x00, 0x00, 0x00, 0x00, 0x08, 0x00, 0x00, 0x00];
        var ex = Assert.Throws<FormatException>(() => TimDecoder.Decode(bad));
        Assert.Contains("magic", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Decode_UnsupportedFlags_ThrowsFormatException()
    {
        // flags = 0x0B (not in our supported set)
        byte[] data = new byte[8];
        BinaryPrimitives.WriteUInt32LittleEndian(data, 0x00000010u);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 0x0000000Bu);
        var ex = Assert.Throws<FormatException>(() => TimDecoder.Decode(data));
        Assert.Contains("flags", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Decode4Bit_TruncatedClut_ThrowsFormatException()
    {
        // Only 8 bytes (too short for CLUT block)
        byte[] data = [0x10, 0x00, 0x00, 0x00, 0x08, 0x00, 0x00, 0x00];
        Assert.Throws<FormatException>(() => TimDecoder.Decode(data));
    }

    [Fact]
    public void Decode_TooShort_ThrowsFormatException()
    {
        var ex = Assert.Throws<FormatException>(() => TimDecoder.Decode([]));
        Assert.Contains("too short", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Decode4Bit_ClutRowOutOfRange_ThrowsFormatException()
    {
        var clut = new ushort[16];
        var tim = Build4Or8BitTim(is4Bit: true, clut, pixelData: [0x00, 0x00], widthWords: 1, height: 1);
        // Only 1 CLUT row (index 0), request row 1
        Assert.Throws<FormatException>(() => TimDecoder.Decode(tim, clutRow: 1));
    }

    // ── Transparency and RGBA colour conversion ────────────────────────────

    [Fact]
    public void Decode16Bit_AllFiveChannelsMaxBlue_CorrectB()
    {
        // B=31, R=0, G=0, STP=0 → word = 0x7C00
        var tim = Build16BitTim(1, 1, pixels: [0x7C00]);
        var img = TimDecoder.Decode(tim);
        Assert.Equal(0, img.Pixels[0]);   // R = 0
        Assert.Equal(0, img.Pixels[1]);   // G = 0
        Assert.Equal(255, img.Pixels[2]); // B = 255
        Assert.Equal(255, img.Pixels[3]); // A = 255
    }

    [Fact]
    public void Decode16Bit_HalfBrightness_CorrectScaling()
    {
        // R=15 (half of 31), G=0, B=0 → word = 0x000F
        // Expected R = 15 * 255 / 31 = 123
        var tim = Build16BitTim(1, 1, pixels: [0x000F]);
        var img = TimDecoder.Decode(tim);
        Assert.Equal(123, img.Pixels[0]); // R = 123
    }
}
