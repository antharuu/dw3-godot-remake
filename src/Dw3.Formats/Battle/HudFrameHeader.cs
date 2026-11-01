using System.Buffers.Binary;

namespace Dw3.Formats.Battle;

/// <summary>
/// Header of one SpriteDrawer frame record: three s16 values followed by
/// <see cref="Count"/> part records of three s16 (part index, dx, dy).
/// </summary>
/// <param name="Count">Number of part records, 1..64.</param>
/// <param name="Row">CLUT row added to every part's own CLUT row (palette animation).</param>
/// <param name="Abr">Semi-transparency mode; non-zero enables STP blending of palette colours.</param>
public readonly record struct HudFrameHeader(short Count, short Row, short Abr)
{
    /// <summary>Size of the header and of each part record, in bytes.</summary>
    public const int RecordBytes = 6;

    /// <summary>Largest part count accepted in one frame.</summary>
    private const int MaximumParts = 64;

    /// <summary>Reads and validates the header at <paramref name="frameOffset"/>.</summary>
    /// <param name="sheetBody">Sheet body bytes (starting 4 bytes into the raw file).</param>
    /// <param name="frameOffset">Offset into <paramref name="sheetBody"/> of the frame record.</param>
    /// <returns>The decoded header.</returns>
    /// <exception cref="FormatException">The header lies outside the body or the part count is not 1..64.</exception>
    public static HudFrameHeader Read(ReadOnlySpan<byte> sheetBody, int frameOffset)
    {
        if (frameOffset < 0 || frameOffset + RecordBytes > sheetBody.Length)
        {
            throw new FormatException("Sprite frame offset is out of range.");
        }

        short count = BinaryPrimitives.ReadInt16LittleEndian(sheetBody.Slice(frameOffset));
        short frameRow = BinaryPrimitives.ReadInt16LittleEndian(sheetBody.Slice(frameOffset + 2));
        short abr = BinaryPrimitives.ReadInt16LittleEndian(sheetBody.Slice(frameOffset + 4));
        if (count <= 0 || count > MaximumParts)
        {
            throw new FormatException($"Sprite frame has invalid part count {count}.");
        }

        return new HudFrameHeader(count, frameRow, abr);
    }
}
