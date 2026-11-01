using System.Buffers.Binary;
using Dw3.Formats.Text;
using Xunit;

namespace Dw3.Formats.Tests.Text;

/// <summary>Synthetic (disc-free) tests for <see cref="TextTableDecoder"/>.</summary>
public sealed class TextTableDecoderTests
{
    // Build a minimal one-entry FSTALKNN.BIN.
    // Layout: u32 count=1, u32 offset=8, string bytes, 0x00.
    private static byte[] OneEntryTable(params byte[] stringBytes)
    {
        int stringOffset = 4 + 4; // after count + one offset
        var data = new byte[stringOffset + stringBytes.Length + 1];
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0), 1u);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), (uint)stringOffset);
        stringBytes.CopyTo(data, stringOffset);
        data[stringOffset + stringBytes.Length] = 0x00; // null terminator
        return data;
    }

    [Fact]
    public void DecodeFile_EmptyEntries_ReturnsZero()
    {
        // count=0 → empty table
        var data = new byte[] { 0, 0, 0, 0 };
        var entries = TextTableDecoder.DecodeFile(data);
        Assert.Empty(entries);
    }

    [Fact]
    public void DecodeFile_SingleGlyph_ReturnsGlyphToken()
    {
        var data = OneEntryTable(0x12);
        var entries = TextTableDecoder.DecodeFile(data);
        Assert.Single(entries);
        var token = Assert.Single(entries[0]);
        Assert.Equal(TextTokenKind.Glyph, token.Kind);
        Assert.Equal(0x12, token.Code);
    }

    [Fact]
    public void Tokenize_IconThenControl_ReturnsCorrectTokens()
    {
        // 0x01 0x2A  (icon 42)
        // 0x02 0x01  (control cmd=1, length=2, no args)
        byte[] bytes = [0x01, 0x2A, 0x02, 0x01];
        var tokens = TextTableDecoder.Tokenize(bytes, 0, bytes.Length);
        Assert.Equal(2, tokens.Count);
        Assert.Equal(TextTokenKind.Icon, tokens[0].Kind);
        Assert.Equal(0x2A, tokens[0].Code);
        Assert.Equal(TextTokenKind.Control, tokens[1].Kind);
        Assert.Equal(1, tokens[1].Code);
        Assert.Empty(tokens[1].Args);
    }

    [Fact]
    public void Tokenize_ControlWithArgs_ReturnsArgsCorrectly()
    {
        // 0x02 0x05 0x01  (control cmd=5, length=3, one arg=1)
        byte[] bytes = [0x02, 0x05, 0x01];
        var tokens = TextTableDecoder.Tokenize(bytes, 0, bytes.Length);
        var token = Assert.Single(tokens);
        Assert.Equal(TextTokenKind.Control, token.Kind);
        Assert.Equal(5, token.Code);
        Assert.Equal([1], token.Args);
    }

    [Fact]
    public void Tokenize_ReservedByte03_Throws()
    {
        Assert.Throws<FormatException>(() =>
            TextTableDecoder.Tokenize([0x03], 0, 1));
    }

    [Fact]
    public void Tokenize_TruncatedControlId_Throws()
    {
        Assert.Throws<FormatException>(() =>
            TextTableDecoder.Tokenize([0x02], 0, 1)); // no cmd byte
    }

    [Fact]
    public void Tokenize_UnknownControl_Throws()
    {
        Assert.Throws<FormatException>(() =>
            TextTableDecoder.Tokenize([0x02, 0x0A], 0, 2)); // cmd=10 is unknown
    }

    [Fact]
    public void Tokenize_TruncatedControlBody_Throws()
    {
        // cmd=5 needs 3 bytes total (0x02 + cmd + 1 arg), but only 2 bytes available
        Assert.Throws<FormatException>(() =>
            TextTableDecoder.Tokenize([0x02, 0x05], 0, 2));
    }

    [Fact]
    public void Tokenize_TruncatedIcon_Throws()
    {
        Assert.Throws<FormatException>(() =>
            TextTableDecoder.Tokenize([0x01], 0, 1)); // icon byte with no index
    }

    [Fact]
    public void DecodeFile_TruncatedHeader_Throws()
    {
        // count=2 but only one offset in buffer
        var data = new byte[8]; // 4+4 bytes: count=2 would need 4+8=12 bytes
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0), 2u);
        Assert.Throws<FormatException>(() => TextTableDecoder.DecodeFile(data));
    }

    [Fact]
    public void DecodeFile_OffsetOutsidePayload_Throws()
    {
        // count=1, offset pointing before the header end
        var data = new byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0), 1u);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 0u); // 0 < headerSize(8)
        Assert.Throws<FormatException>(() => TextTableDecoder.DecodeFile(data));
    }

    [Fact]
    public void DecodeFile_MissingTerminator_Throws()
    {
        // count=1, offset=8, no null terminator before next bound (=end of file)
        var data = new byte[10];
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(0), 1u);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(4), 8u);
        data[8] = 0x12; // glyph — but no null at [9], and [9] is outside the one-entry bound (10 = end of file)
        data[9] = 0x34; // another glyph, no null terminator anywhere before EOF
        Assert.Throws<FormatException>(() => TextTableDecoder.DecodeFile(data));
    }

    [Fact]
    public void DecodeFile_MultipleEntries_ReturnsAllCorrectly()
    {
        // Two entries: entry 0 = [0x12], entry 1 = [0x34, 0x56]
        // offsets: 4+8=12 as header, then strings at 12 and 14.
        byte[] data = [
            2, 0, 0, 0,             // count=2
            12, 0, 0, 0,            // offset[0] = 12
            14, 0, 0, 0,            // offset[1] = 14
            0x12, 0x00,             // entry 0: [0x12], null
            0x34, 0x56, 0x00        // entry 1: [0x34, 0x56], null
        ];
        var entries = TextTableDecoder.DecodeFile(data);
        Assert.Equal(2, entries.Count);
        _ = Assert.Single(entries[0]);
        Assert.Equal(0x12, entries[0][0].Code);
        Assert.Equal(2, entries[1].Count);
        Assert.Equal(0x56, entries[1][1].Code);
    }
}
