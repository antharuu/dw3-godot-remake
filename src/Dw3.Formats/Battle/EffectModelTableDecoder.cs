using System.Buffers.Binary;

namespace Dw3.Formats.Battle;

/// <summary>Decodes the terminated registry of shared three-dimensional effect models in the supported battle overlay.</summary>
public static class EffectModelTableDecoder
{
    /// <summary>Gets the byte offset of the European battle-overlay effect-model registry.</summary>
    public const int TableOffset = 0x1F8A0;

    /// <summary>Gets the byte width of one effect-model registry entry.</summary>
    public const int EntryBytes = 12;

    /// <summary>Reads the effect-model registry until its zero identifier terminator.</summary>
    /// <param name="overlay">Raw bytes of the supported European FIGHTSTG overlay.</param>
    /// <returns>Effect-model references in overlay order, without the terminator.</returns>
    /// <exception cref="FormatException">The registry is truncated or has no zero identifier terminator.</exception>
    public static IReadOnlyList<EffectModelReference> Decode(ReadOnlySpan<byte> overlay)
    {
        if (overlay.Length < TableOffset + EntryBytes)
        {
            throw new FormatException("Battle effect-model registry is truncated.");
        }

        var references = new List<EffectModelReference>();
        var byId = new Dictionary<int, EffectModelReference>();
        for (int offset = TableOffset; offset + EntryBytes <= overlay.Length; offset += EntryBytes)
        {
            int id = BinaryPrimitives.ReadInt32LittleEndian(overlay.Slice(offset, 4));
            if (id == 0)
            {
                return references;
            }

            uint model = BinaryPrimitives.ReadUInt32LittleEndian(overlay.Slice(offset + 4, 4));
            uint motion = BinaryPrimitives.ReadUInt32LittleEndian(overlay.Slice(offset + 8, 4));
            var reference = new EffectModelReference(id, (int)(model >> 16), (int)(model & 0xffff), (int)(motion >> 16), (int)(motion & 0xffff));
            if (byId.TryGetValue(id, out EffectModelReference? existing))
            {
                if (existing != reference)
                {
                    throw new FormatException($"Battle effect-model registry has conflicting entries for id {id}.");
                }

                continue;
            }

            byId.Add(id, reference);
            references.Add(reference);
        }

        throw new FormatException("Battle effect-model registry has no zero identifier terminator.");
    }
}
