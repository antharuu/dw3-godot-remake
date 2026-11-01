using Dw3.Formats.Binary;
using Xunit;

namespace Dw3.Formats.Tests.Binary;

public sealed class BinarySpanReaderTests
{
    // ── ReadU8 ────────────────────────────────────────────────────────────

    [Fact]
    public void ReadU8_ReturnsCorrectByte()
    {
        var r = new BinarySpanReader([0xAB, 0xCD]);
        Assert.Equal(0xAB, r.ReadU8(0));
        Assert.Equal(0xCD, r.ReadU8(1));
    }

    [Fact]
    public void ReadU8_AtLastByte_Succeeds()
    {
        var r = new BinarySpanReader([0xFF]);
        Assert.Equal(0xFF, r.ReadU8(0));
    }

    [Fact]
    public void ReadU8_OutOfRange_ThrowsFormatException()
    {
        var r = new BinarySpanReader([0x01]);
        var ex = Assert.Throws<FormatException>(() => r.ReadU8(1));
        Assert.Contains("0x1", ex.Message);
    }

    // ── ReadU16 ───────────────────────────────────────────────────────────

    [Fact]
    public void ReadU16_LittleEndian()
    {
        var r = new BinarySpanReader([0x34, 0x12]);
        Assert.Equal(0x1234, r.ReadU16(0));
    }

    [Fact]
    public void ReadU16_AtBoundary_Succeeds()
    {
        var r = new BinarySpanReader([0x00, 0xFF, 0xAA]);
        Assert.Equal(0xAA_FF, r.ReadU16(1));
    }

    [Fact]
    public void ReadU16_PartiallyOutOfRange_ThrowsFormatException()
    {
        var r = new BinarySpanReader([0x01, 0x02]);
        // Needs 2 bytes at offset 1 but only 1 is available
        var ex = Assert.Throws<FormatException>(() => r.ReadU16(1));
        Assert.Contains("0x1", ex.Message);
    }

    [Fact]
    public void ReadU16_NegativeOffset_ThrowsFormatException()
    {
        var r = new BinarySpanReader([0x01, 0x02]);
        Assert.Throws<FormatException>(() => r.ReadU16(-1));
    }

    // ── ReadU32 ───────────────────────────────────────────────────────────

    [Fact]
    public void ReadU32_LittleEndian()
    {
        var r = new BinarySpanReader([0x78, 0x56, 0x34, 0x12]);
        Assert.Equal(0x12345678u, r.ReadU32(0));
    }

    [Fact]
    public void ReadU32_ExactlyFourBytes_Succeeds()
    {
        var r = new BinarySpanReader([0x01, 0x00, 0x00, 0x00]);
        Assert.Equal(1u, r.ReadU32(0));
    }

    [Fact]
    public void ReadU32_PartiallyOutOfRange_ThrowsFormatException()
    {
        var r = new BinarySpanReader([0x01, 0x02, 0x03]);
        var ex = Assert.Throws<FormatException>(() => r.ReadU32(0));
        Assert.Contains("0x0", ex.Message);
    }

    // ── ReadI32 ───────────────────────────────────────────────────────────

    [Fact]
    public void ReadI32_NegativeValue()
    {
        var r = new BinarySpanReader([0xFF, 0xFF, 0xFF, 0xFF]);
        Assert.Equal(-1, r.ReadI32(0));
    }

    [Fact]
    public void ReadI32_OutOfRange_ThrowsFormatException()
    {
        var r = new BinarySpanReader([0x01, 0x02]);
        Assert.Throws<FormatException>(() => r.ReadI32(0));
    }

    // ── ReadI16 ───────────────────────────────────────────────────────────

    [Fact]
    public void ReadI16_NegativeValue()
    {
        var r = new BinarySpanReader([0x00, 0x80]);
        Assert.Equal(short.MinValue, r.ReadI16(0));
    }

    [Fact]
    public void ReadI16_OutOfRange_ThrowsFormatException()
    {
        var r = new BinarySpanReader([0x01]);
        Assert.Throws<FormatException>(() => r.ReadI16(0));
    }

    // ── Slice ─────────────────────────────────────────────────────────────

    [Fact]
    public void Slice_ReturnsCopy()
    {
        var r = new BinarySpanReader([0xAA, 0xBB, 0xCC]);
        var s = r.Slice(1, 2);
        Assert.Equal([0xBB, 0xCC], s);
    }

    [Fact]
    public void Slice_ZeroLength_ReturnsEmptyArray()
    {
        var r = new BinarySpanReader([0x01]);
        Assert.Empty(r.Slice(0, 0));
    }

    [Fact]
    public void Slice_OutOfRange_ThrowsFormatException()
    {
        var r = new BinarySpanReader([0x01, 0x02]);
        Assert.Throws<FormatException>(() => r.Slice(1, 2));
    }

    // ── Length and null guard ─────────────────────────────────────────────

    [Fact]
    public void Length_ReflectsBufferSize()
    {
        var r = new BinarySpanReader([1, 2, 3]);
        Assert.Equal(3, r.Length);
    }

    [Fact]
    public void Constructor_NullData_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new BinarySpanReader(null!));
    }
}
