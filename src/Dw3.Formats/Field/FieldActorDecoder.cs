using System.Buffers.Binary;

namespace Dw3.Formats.Field;

/// <summary>
/// Decodes a FieldActorEntry record from a raw field overlay.
/// A FieldActorEntry is a 20-byte struct at a fixed overlay RAM address.
/// Only the three actor width IDs present in the two supported collision
/// stages (S200, S235) are accepted; unknown IDs fail explicitly.
///
/// Disc layout (FieldActorEntry, little-endian):
///   +0x00  u32  conditions pointer (skipped)
///   +0x04  u32  talks pointer (skipped)
///   +0x08  i16  actor_id
///   +0x0A  i16  image (skipped)
///   +0x0C  i16  x  (pixel x position on the stage map)
///   +0x0E  i16  y  (pixel y position on the stage map)
///   +0x10  i16  direction (0..7, skipped here)
///
/// Width table (FIELDSTG_actorWidths in the disc executable,
/// three entries verified from the S200 and S235 overlays):
///   0x14 → 32 pixels
///   0x15 → 48 pixels
///   0x2E → 32 pixels
/// </summary>
public static class FieldActorDecoder
{
    private const int RecordBytes = 18; // bytes used (conditions+talks+actor_id+image+x+y+dir)
    private const int ConditionsOffset = 0;
    private const int ActorIdOffset = 8;
    private const int XOffset = 12;
    private const int YOffset = 14;

    // Three actor width IDs verified from S200 and S235 overlay data sections.
    // These are entries from the disc executable's FIELDSTG_actorWidths array.
    private static readonly IReadOnlyDictionary<short, int> ActorWidths =
        new Dictionary<short, int>
        {
            [0x14] = 32,
            [0x15] = 48,
            [0x2E] = 32,
        };

    /// <summary>
    /// Decodes a FieldActorEntry at the given overlay RAM address.
    /// </summary>
    /// <param name="overlay">Raw overlay bytes (the .PRO file contents).</param>
    /// <param name="actorAddress">RAM address of the actor entry (from the overlay's load base).</param>
    /// <param name="overlayLoadBase">RAM address at which the overlay is loaded (disc format constant).</param>
    /// <returns>The decoded actor record.</returns>
    public static FieldActor Decode(ReadOnlySpan<byte> overlay, uint actorAddress, uint overlayLoadBase)
    {
        if (actorAddress < overlayLoadBase)
            throw new FormatException($"Actor address 0x{actorAddress:X8} is below the overlay load base 0x{overlayLoadBase:X8}.");

        int fileOffset = checked((int)(actorAddress - overlayLoadBase));
        if (fileOffset + RecordBytes > overlay.Length)
            throw new FormatException($"Actor record at overlay offset 0x{fileOffset:X} extends beyond the overlay ({overlay.Length} bytes).");

        short actorId = BinaryPrimitives.ReadInt16LittleEndian(overlay.Slice(fileOffset + ActorIdOffset, 2));
        if (!ActorWidths.TryGetValue(actorId, out int width))
            throw new FormatException($"Unsupported actor width ID 0x{actorId:X2}; only IDs 0x14, 0x15 and 0x2E are supported.");

        short x = BinaryPrimitives.ReadInt16LittleEndian(overlay.Slice(fileOffset + XOffset, 2));
        short y = BinaryPrimitives.ReadInt16LittleEndian(overlay.Slice(fileOffset + YOffset, 2));

        return new FieldActor(x, y, width);
    }
}

/// <summary>Decoded actor position and width for occupancy checking.</summary>
public sealed record FieldActor(short X, short Y, int Width);
