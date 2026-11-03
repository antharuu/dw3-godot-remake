using Dw3.Formats.Field;
using Xunit;

namespace Dw3.Formats.Tests.Field;

/// <summary>
/// Synthetic tests for FieldActorDecoder: decodes a FieldActorEntry record from
/// a raw overlay byte slice, using the overlay-relative address and the overlay
/// load base address as the only two inputs.
/// </summary>
public sealed class FieldActorDecoderTests
{
    // FieldActorEntry layout (overlay disc format):
    //   +0x00  u32  conditions pointer (not used by decoder)
    //   +0x04  u32  talks pointer (not used by decoder)
    //   +0x08  i16  actor_id
    //   +0x0A  i16  image
    //   +0x0C  i16  x
    //   +0x0E  i16  y
    //   +0x10  i16  direction
    // Total: 18 bytes used of the 20-byte record.
    // actor widths come from the disc's actor-width table; only the
    // three known width IDs in the two supported stages are accepted.
    [Fact]
    public void DecodesActorRecordAtKnownAddress()
    {
        // Fabricated overlay bytes: place the actor at address 0x800A0010,
        // overlay load base = 0x800A0000, so file offset = 0x10.
        // conditions=0, talks=0, actor_id=0x14 (width 32), image=4, x=100, y=200, dir=3.
        const uint loadBase = 0x800A0000u;
        const uint actorAddress = 0x800A0010u;
        var overlay = new byte[0x30];
        int offset = (int)(actorAddress - loadBase);
        WriteUInt32(overlay, offset + 0, 0);          // conditions
        WriteUInt32(overlay, offset + 4, 0);          // talks
        WriteInt16(overlay, offset + 8, 0x14);        // actor_id = 0x14 → width 32
        WriteInt16(overlay, offset + 10, 4);          // image
        WriteInt16(overlay, offset + 12, 100);        // x
        WriteInt16(overlay, offset + 14, 200);        // y
        WriteInt16(overlay, offset + 16, 3);          // direction
        var actor = FieldActorDecoder.Decode(overlay, actorAddress, loadBase);
        Assert.Equal(100, actor.X);
        Assert.Equal(200, actor.Y);
        Assert.Equal(32, actor.Width);
    }

    [Fact]
    public void RejectsAddressOutsideOverlay()
    {
        var overlay = new byte[16];
        Assert.Throws<FormatException>(() =>
            FieldActorDecoder.Decode(overlay, 0x800A0020u, 0x800A0000u));
    }

    [Fact]
    public void RejectsUnsupportedActorId()
    {
        const uint loadBase = 0x800A0000u;
        const uint actorAddress = 0x800A0000u;
        var overlay = new byte[0x20];
        WriteInt16(overlay, 8, 0x01); // actor_id = 0x01 → not in the width table
        Assert.Throws<FormatException>(() =>
            FieldActorDecoder.Decode(overlay, actorAddress, loadBase));
    }

    private static void WriteUInt32(byte[] buf, int offset, uint value)
    {
        buf[offset] = (byte)(value & 0xFF);
        buf[offset + 1] = (byte)((value >> 8) & 0xFF);
        buf[offset + 2] = (byte)((value >> 16) & 0xFF);
        buf[offset + 3] = (byte)((value >> 24) & 0xFF);
    }

    private static void WriteInt16(byte[] buf, int offset, int value)
    {
        buf[offset] = (byte)(value & 0xFF);
        buf[offset + 1] = (byte)((value >> 8) & 0xFF);
    }
}
