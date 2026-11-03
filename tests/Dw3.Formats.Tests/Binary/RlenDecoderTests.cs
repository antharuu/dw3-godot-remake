using Dw3.Formats.Binary;
using Xunit;

namespace Dw3.Formats.Tests.Binary;

public sealed class RlenDecoderTests
{
    // ── IsRlen ────────────────────────────────────────────────────────────
    [Fact]
    public void IsRlen_ValidMagic_ReturnsTrue()
    {
        var data = BuildRlen(0);
        Assert.True(RlenDecoder.IsRlen(data));
    }

    [Fact]
    public void IsRlen_EmptyBuffer_ReturnsFalse()
    {
        Assert.False(RlenDecoder.IsRlen([]));
    }

    [Fact]
    public void IsRlen_WrongMagic_ReturnsFalse()
    {
        Assert.False(RlenDecoder.IsRlen([0x00, 0x01, 0x02, 0x03]));
    }

    // ── Empty stream (zero target size) ───────────────────────────────────
    [Fact]
    public void Decompress_EmptyStream_ReturnsEmptyArray()
    {
        var data = BuildRlen(0);
        var result = RlenDecoder.Decompress(data);
        Assert.Empty(result);
    }

    // ── Literal run ───────────────────────────────────────────────────────
    [Fact]
    public void Decompress_LiteralRun_CopiesVerbatim()
    {
        // Command 0x03 = literal run of 3 bytes
        var data = BuildRlen(3, 0x03, 0xAA, 0xBB, 0xCC);
        var result = RlenDecoder.Decompress(data);
        Assert.Equal([0xAA, 0xBB, 0xCC], result);
    }

    [Fact]
    public void Decompress_LiteralRunMaxSize_Succeeds()
    {
        // Command 0x7F = literal run of 127 bytes
        var payload = new byte[127];
        for (int i = 0; i < 127; i++)
        {
            payload[i] = (byte)i;
        }

        var stream = new List<byte>
        {
            (byte)'R', (byte)'L', (byte)'E', (byte)'N',
            127, 0, 0, 0,  // target = 127
            0x7F, // literal 127
        };
        stream.AddRange(payload);
        stream.Add(0); // terminator
        var result = RlenDecoder.Decompress([.. stream]);
        Assert.Equal(payload, result);
    }

    // ── Repeat run ────────────────────────────────────────────────────────
    [Fact]
    public void Decompress_RepeatRun_FillsRepeatedByte()
    {
        // Command 0x85 = repeat 5 times; byte = 0xFF
        var data = BuildRlen(5, 0x85, 0xFF);
        var result = RlenDecoder.Decompress(data);
        Assert.Equal([0xFF, 0xFF, 0xFF, 0xFF, 0xFF], result);
    }

    [Fact]
    public void Decompress_RepeatRun_SingleByte()
    {
        var data = BuildRlen(1, 0x81, 0x42);
        var result = RlenDecoder.Decompress(data);
        Assert.Equal([0x42], result);
    }

    // ── Mixed literal + repeat ────────────────────────────────────────────
    [Fact]
    public void Decompress_Mixed_ProducesCorrectOutput()
    {
        // literal 2: 0x11 0x22, then repeat 3: 0xAA
        var data = BuildRlen(5, 0x02, 0x11, 0x22, 0x83, 0xAA);
        var result = RlenDecoder.Decompress(data);
        Assert.Equal([0x11, 0x22, 0xAA, 0xAA, 0xAA], result);
    }

    // ── DecompressIfNeeded ────────────────────────────────────────────────
    [Fact]
    public void DecompressIfNeeded_Rlen_ReturnsDecompressedAndTrue()
    {
        var data = BuildRlen(2, 0x02, 0x55, 0x66);
        var (result, wasCompressed) = RlenDecoder.DecompressIfNeeded(data);
        Assert.True(wasCompressed);
        Assert.Equal([0x55, 0x66], result);
    }

    [Fact]
    public void DecompressIfNeeded_NotRlen_ReturnsOriginalAndFalse()
    {
        byte[] raw = [0x01, 0x02, 0x03];
        var (result, wasCompressed) = RlenDecoder.DecompressIfNeeded(raw);
        Assert.False(wasCompressed);
        Assert.Equal(raw, result);
    }

    // ── Error cases ───────────────────────────────────────────────────────
    [Fact]
    public void Decompress_BadMagic_ThrowsFormatException()
    {
        byte[] bad = [0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
        var ex = Assert.Throws<FormatException>(() => RlenDecoder.Decompress(bad));
        Assert.Contains("magic", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Decompress_TruncatedHeader_ThrowsFormatException()
    {
        byte[] bad = [(byte)'R', (byte)'L', (byte)'E', (byte)'N', 0x01];
        var ex = Assert.Throws<FormatException>(() => RlenDecoder.Decompress(bad));
        Assert.Contains("truncated", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Decompress_NoTerminator_ThrowsFormatException()
    {
        // Build stream that has a valid literal (1 byte produced), then EOF with no terminator.
        // We declare target=2 so the decoder tries to continue after the literal run.
        byte[] bad = [(byte)'R', (byte)'L', (byte)'E', (byte)'N', 2, 0, 0, 0,
            0x01, 0xAB]; // literal 1 byte, target=2 — decoder needs more but hits EOF
        var ex = Assert.Throws<FormatException>(() => RlenDecoder.Decompress(bad));

        // Either "terminator" or "no terminator" message is acceptable
        Assert.True(
            ex.Message.Contains("terminator", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("size", StringComparison.OrdinalIgnoreCase) ||
            ex.Message.Contains("mismatch", StringComparison.OrdinalIgnoreCase),
            $"Unexpected message: {ex.Message}");
    }

    [Fact]
    public void Decompress_LiteralRunExceedsBuffer_ThrowsFormatException()
    {
        // Command 0x03 = literal 3, but only 1 byte follows before EOF
        byte[] bad = [(byte)'R', (byte)'L', (byte)'E', (byte)'N', 3, 0, 0, 0,
            0x03, 0xAA];
        var ex = Assert.Throws<FormatException>(() => RlenDecoder.Decompress(bad));
        Assert.Contains("exceeds", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Decompress_RepeatLacksValueByte_ThrowsFormatException()
    {
        // Command 0x82 = repeat 2, but no value byte follows
        byte[] bad = [(byte)'R', (byte)'L', (byte)'E', (byte)'N', 2, 0, 0, 0, 0x82];
        var ex = Assert.Throws<FormatException>(() => RlenDecoder.Decompress(bad));
        Assert.Contains("value byte", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Decompress_SizeMismatch_ThrowsFormatException()
    {
        // Target declares 5 bytes, but command only produces 2
        var data = BuildRlen(5, 0x02, 0x11, 0x22);
        var ex = Assert.Throws<FormatException>(() => RlenDecoder.Decompress(data));
        Assert.Contains("2", ex.Message);
        Assert.Contains("5", ex.Message);
    }

    [Fact]
    public void Decompress_OutputExceedsDeclaredSize_ThrowsFormatException()
    {
        // Target declares 2 bytes, but command produces 5
        var data = BuildRlen(2, 0x85, 0xFF); // repeat 5 × 0xFF
        var ex = Assert.Throws<FormatException>(() => RlenDecoder.Decompress(data));
        Assert.Contains("exceed", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>Builds a minimal valid RLEN stream from commands.</summary>
    private static byte[] BuildRlen(uint targetSize, params byte[] commands)
    {
        var stream = new List<byte>
        {
            (byte)'R',
            (byte)'L',
            (byte)'E',
            (byte)'N',
            (byte)(targetSize & 0xFF),
            (byte)((targetSize >> 8) & 0xFF),
            (byte)((targetSize >> 16) & 0xFF),
            (byte)((targetSize >> 24) & 0xFF),
        };
        stream.AddRange(commands);
        stream.Add(0); // terminator
        return [.. stream];
    }
}
