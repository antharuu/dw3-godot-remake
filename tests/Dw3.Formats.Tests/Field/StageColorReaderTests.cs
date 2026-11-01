using Xunit;
using Dw3.Formats.Field;

namespace Dw3.Formats.Tests.Field;

/// <summary>Synthetic tests for StageColorReader.</summary>
public sealed class StageColorReaderTests
{
    [Fact]
    public void ReturnsColorWhenFirstFourBytesAreKnownCvector()
    {
        // Fabricated overlay whose first 4 bytes are the sea-blue color {54, 67, 96, 00}.
        // opcode = word >> 26 = 0x00966754 >> 26 = 0 → SPECIAL → data, not code.
        var overlay = new byte[8];
        overlay[0] = 84;
        overlay[1] = 103;
        overlay[2] = 150;
        overlay[3] = 0;

        byte[]? color = StageColorReader.TryRead(overlay);

        Assert.NotNull(color);
        Assert.Equal(new byte[] { 84, 103, 150 }, color);
    }

    [Fact]
    public void ReturnsNullWhenFirstFourBytesAreMipsInstruction()
    {
        // Fabricated overlay whose first 4 bytes are ADDIU $sp, $sp, -0x18 (opcode 9, != 0).
        // Little-endian 0x27BDFFE8 → bytes [0xE8, 0xFF, 0xBD, 0x27].
        var overlay = new byte[8];
        overlay[0] = 0xE8;
        overlay[1] = 0xFF;
        overlay[2] = 0xBD;
        overlay[3] = 0x27;

        byte[]? color = StageColorReader.TryRead(overlay);

        Assert.Null(color);
    }

    [Fact]
    public void ReturnsNullForTooShortOverlay()
    {
        Assert.Null(StageColorReader.TryRead(new byte[2]));
    }

    [Fact]
    public void ReturnsGrayColorAtOffset0x90WhenOffset0IsMipsCode()
    {
        // Overlay that has opcode 0x20 (LB) at offset 0 (code), and the gray CVECTOR at 0x90.
        var overlay = new byte[0x98];
        // offset 0: LB instruction (opcode 32 = 0x20), upper 6 bits = 100000
        overlay[3] = 0x80; // high byte: 10000000 → top 6 bits = 100000 = 0x20
        // offset 0x90: gray CVECTOR {128, 128, 128, 0} = 0x00808080
        overlay[0x90] = 128;
        overlay[0x91] = 128;
        overlay[0x92] = 128;
        overlay[0x93] = 0;

        byte[]? color = StageColorReader.TryRead(overlay);

        Assert.NotNull(color);
        Assert.Equal(new byte[] { 128, 128, 128 }, color);
    }
}
