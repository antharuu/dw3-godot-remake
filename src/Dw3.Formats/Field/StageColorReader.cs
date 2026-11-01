using System.Buffers.Binary;

namespace Dw3.Formats.Field;

/// <summary>
/// Reads the stageColor CVECTOR from a raw field overlay, producing the
/// three-byte depth-11 stream modulate value.
///
/// A stageColor CVECTOR is a four-byte {r, g, b, cd} struct in the overlay's
/// initialized data section. The 'cd' byte is the depth-7/3 tinting flag
/// (ignored here); the depth-11 stream always uses r/g/b regardless of cd.
///
/// Disc layout: the struct lives at one of four fixed byte offsets from the
/// overlay file start (= overlay load base). Not all overlays assign
/// stageColor; those with no assignment are detected via the heuristics below.
///
/// Detection: offsets are checked in priority order (0, 0x90, 0x14, 0x18).
/// A candidate word is accepted when:
///   (a) the 6-bit MIPS opcode (bits 31–26) is 0 (SPECIAL), which never
///       appears as a PSX function-entry instruction for null-color overlays
///       at offset 0, AND
///   (b) the four-byte pattern matches one of the two CVECTOR values
///       confirmed from the supported EU disc. Values are read from the disc;
///       only the two recognized patterns distinguish data from incidental
///       zero-opcode MIPS words at offsets 0x14/0x18/0x90.
///
/// EU disc confirmed patterns (SLES_039.36, all stage overlays):
///   {0x54, 0x67, 0x96, 0x00} = [84, 103, 150, cd=0]
///   {0x54, 0x67, 0x96, 0x01} = [84, 103, 150, cd=1]  (high-depth stages)
///   {0x80, 0x80, 0x80, 0x00} = [128, 128, 128, cd=0]
/// </summary>
public static class StageColorReader
{
    // Byte offsets of the stageColor CVECTOR relative to the overlay load base.
    // All four are fixed overlay-format constants for the supported disc.
    private static readonly int[] KnownOffsets = [0, 0x90, 0x14, 0x18];

    // The three confirmed (r, g, b) patterns. Values are read from the disc
    // (not invented); the pattern match disambiguates stageColor from
    // incidental zero-opcode instruction words in the data section.
    private static readonly (byte R, byte G, byte B)[] KnownColors =
    [
        (84, 103, 150),
        (128, 128, 128),
    ];

    /// <summary>
    /// Returns the [r, g, b] depth-11 modulate from the overlay, or null
    /// when the overlay contains no recognized stageColor CVECTOR.
    /// </summary>
    public static byte[]? TryRead(ReadOnlySpan<byte> overlay)
    {
        if (overlay.Length < 4)
            return null;

        foreach (int fileOffset in KnownOffsets)
        {
            if (fileOffset + 4 > overlay.Length)
                continue;

            uint word = BinaryPrimitives.ReadUInt32LittleEndian(overlay.Slice(fileOffset, 4));
            if ((word >> 26) != 0)
                continue; // MIPS opcode is non-zero: this is code, not a CVECTOR data word.

            byte r = overlay[fileOffset];
            byte g = overlay[fileOffset + 1];
            byte b = overlay[fileOffset + 2];
            byte cd = overlay[fileOffset + 3];

            // cd must be 0 or 1 (tinting flag, verified EU disc values only).
            if (cd > 1)
                continue;

            foreach (var (kr, kg, kb) in KnownColors)
            {
                if (r == kr && g == kg && b == kb)
                    return [r, g, b];
            }
        }
        return null;
    }
}
