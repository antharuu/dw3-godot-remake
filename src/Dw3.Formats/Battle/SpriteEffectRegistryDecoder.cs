using System.Buffers.Binary;

namespace Dw3.Formats.Battle;

/// <summary>Decodes the supported battle-overlay registry that maps script-visible 2D sprite ids to sheet and archive resources.</summary>
public static class SpriteEffectRegistryDecoder
{
    /// <summary>Gets the European overlay byte offset of the effect-sheet registry.</summary>
    public const int EffectSheetsOffset = 0x1FEC4;

    /// <summary>Gets the European overlay byte offset of the sprite-effect registry.</summary>
    public const int SpriteEffectsOffset = 0x201F4;

    /// <summary>Gets the byte width of one terminated sprite-effect registry entry.</summary>
    public const int SpriteEffectEntryBytes = 8;

    /// <summary>Reads one bounded sixteen-byte sheet registry entry.</summary>
    /// <param name="overlay">Supported battle overlay bytes.</param>
    /// <param name="index">Sheet registry index.</param>
    /// <returns>Archive selectors and source placement words.</returns>
    public static SpriteEffectSheetReference DecodeSheet(ReadOnlySpan<byte> overlay, int index)
    {
        int count = (SpriteEffectsOffset - EffectSheetsOffset) / 16;
        if (index < 0 || index >= count || overlay.Length < EffectSheetsOffset + ((index + 1) * 16))
        {
            throw new FormatException("Sprite sheet registry index is out of bounds.");
        }

        ReadOnlySpan<byte> record = overlay.Slice(EffectSheetsOffset + (index * 16), 16);
        var placement = new int[2];
        for (int i = 0; i < placement.Length; i++)
        {
            placement[i] = BinaryPrimitives.ReadInt32LittleEndian(record[(8 + (i * 4))..]);
        }

        return new SpriteEffectSheetReference(index, BinaryPrimitives.ReadUInt32LittleEndian(record), BinaryPrimitives.ReadUInt32LittleEndian(record[4..]), placement);
    }

    /// <summary>Reads sprite-effect records through their required negative-one identifier terminator.</summary>
    /// <param name="overlay">Raw bytes of the supported European battle overlay.</param>
    /// <returns>Unique source-order sprite resource references.</returns>
    /// <exception cref="FormatException">The registry is truncated, conflicting, or unterminated.</exception>
    public static IReadOnlyList<SpriteEffectReference> DecodeSpriteEffects(ReadOnlySpan<byte> overlay)
    {
        if (overlay.Length < SpriteEffectsOffset + SpriteEffectEntryBytes)
        {
            throw new FormatException("Battle sprite-effect registry is truncated.");
        }

        var results = new List<SpriteEffectReference>();
        var seen = new Dictionary<int, SpriteEffectReference>();
        for (int offset = SpriteEffectsOffset; offset + SpriteEffectEntryBytes <= overlay.Length; offset += SpriteEffectEntryBytes)
        {
            short id = BinaryPrimitives.ReadInt16LittleEndian(overlay.Slice(offset, 2));
            if (id == -1)
            {
                return results;
            }

            short sheet = BinaryPrimitives.ReadInt16LittleEndian(overlay.Slice(offset + 2, 2));
            uint packed = BinaryPrimitives.ReadUInt32LittleEndian(overlay.Slice(offset + 4, 4));
            if (id < 0 || sheet < 0)
            {
                throw new FormatException("Battle sprite-effect registry has an invalid negative identifier.");
            }

            var reference = new SpriteEffectReference(id, sheet, (int)(packed >> 16), (int)(packed & 0xffff));
            if (seen.TryGetValue(id, out SpriteEffectReference? existing))
            {
                if (existing != reference)
                {
                    throw new FormatException($"Battle sprite-effect registry has conflicting entries for id {id}.");
                }

                continue;
            }

            seen.Add(id, reference);
            results.Add(reference);
        }

        throw new FormatException("Battle sprite-effect registry has no -1 identifier terminator.");
    }
}
