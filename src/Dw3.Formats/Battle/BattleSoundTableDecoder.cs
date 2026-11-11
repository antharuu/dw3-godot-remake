using System.Buffers.Binary;

namespace Dw3.Formats.Battle;

/// <summary>Reads the EU action-sound index table as data, independently of script and VAB indices.</summary>
public static class BattleSoundTableDecoder
{
    /// <summary>Gets the supported overlay's sound-table byte offset.</summary>
    public const int TableOffset = 0x209DC;

    /// <summary>Gets the number of index entries, including the music and stop control slots.</summary>
    public const int EntryCount = 92;

    /// <summary>Reads encoded identifiers from a supported battle overlay.</summary>
    /// <param name="overlay">User-disc battle overlay bytes.</param>
    /// <returns>The raw encoded identifiers, indexed by script sound index.</returns>
    public static IReadOnlyList<uint> Decode(ReadOnlySpan<byte> overlay)
    {
        if (overlay.Length < TableOffset + (EntryCount * 4))
        {
            throw new FormatException("Battle sound index table is truncated.");
        }

        var result = new uint[EntryCount];
        for (int index = 0; index < result.Length; index++)
        {
            result[index] = BinaryPrimitives.ReadUInt32LittleEndian(overlay.Slice(TableOffset + (index * 4), 4));
        }

        return result;
    }
}
