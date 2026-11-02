using System.Buffers.Binary;

namespace Dw3.Formats.Battle;

/// <summary>
/// Reads fixed-layout battle records. The addresses that locate these records are intentionally
/// owned by the extraction step. This type only validates and decodes byte structures.
/// </summary>
public static class BattleDataDecoder
{
    /// <summary>Byte size of one creature record in the creature data table (0x58 bytes).</summary>
    public const int DigimonStride = 0x58;

    /// <summary>Byte size of one technique record in the technique table (0x12 bytes).</summary>
    public const int TechniqueStride = 0x12;

    /// <summary>Byte size of one enemy battle configuration record (0x46 bytes).</summary>
    public const int BattleTableStride = 0x46;

    /// <summary>Byte size of one evolution prerequisite record (16 bytes).</summary>
    public const int EvolutionStride = 16;

    /// <summary>Byte size of one battle reward record (3 × s32 = 12 bytes).</summary>
    public const int RewardStride = 12;

    /// <summary>Byte size of one item record (12 bytes).</summary>
    public const int ItemStride = 12;

    /// <summary>Byte size of one item effect record (4 bytes).</summary>
    public const int ItemEffectStride = 4;

    /// <summary>
    /// Decodes the battle growth tables from a contiguous region in the data span.
    /// Layout: 4 s32 level-experience thresholds, 4 vital-cut values, 9 vital-random values,
    /// 6 × 9 stat-gain tiers, 8 resistance-gain values — all little-endian s32.
    /// </summary>
    /// <param name="data">Full data span that contains the growth tables.</param>
    /// <param name="offset">Byte offset of the first growth table value within <paramref name="data"/>.</param>
    /// <returns>The decoded growth tables ready for use by the level-up calculator.</returns>
    /// <exception cref="FormatException">The requested region lies outside <paramref name="data"/>.</exception>
    public static BattleGrowthData DecodeGrowth(ReadOnlySpan<byte> data, int offset)
    {
        const int levelCount = 4;
        const int vitalCutsCount = 4;
        const int vitalRandomCount = 9;
        const int statTiers = 6;
        const int statValuesPerTier = 9;
        const int resistanceCount = 8;
        const int totalBytes = (levelCount + vitalCutsCount + vitalRandomCount + statTiers * statValuesPerTier + resistanceCount) * sizeof(int);
        EnsureRange(data, offset, totalBytes, "Growth tables");
        int[] levelExperience = ReadInt32Run(data, offset, levelCount);
        int[] vitalCuts = ReadInt32Run(data, offset + levelCount * sizeof(int), vitalCutsCount);
        int vitalRandomOffset = offset + (levelCount + vitalCutsCount) * sizeof(int);
        int[] vitalRandom = ReadInt32Run(data, vitalRandomOffset, vitalRandomCount);
        int statOffset = vitalRandomOffset + vitalRandomCount * sizeof(int);
        var statGains = new IReadOnlyList<int>[statTiers];
        for (int tier = 0; tier < statTiers; tier++)
        {
            statGains[tier] = ReadInt32Run(data, statOffset + tier * statValuesPerTier * sizeof(int), statValuesPerTier);
        }

        int resistanceOffset = statOffset + statTiers * statValuesPerTier * sizeof(int);
        int[] resistanceGains = ReadInt32Run(data, resistanceOffset, resistanceCount);
        return new BattleGrowthData(levelExperience, vitalCuts, vitalRandom, statGains, resistanceGains);
    }

    /// <summary>
    /// Decodes one item record from <paramref name="data"/> at <paramref name="offset"/>.
    /// Layout: u32 dataPointer, u16 price, u16 sellPrice, u8 kind, u8 type (12 bytes total).
    /// </summary>
    /// <param name="data">Data span that contains the item record.</param>
    /// <param name="offset">Byte offset of the record within <paramref name="data"/>.</param>
    /// <returns>The decoded item record.</returns>
    /// <exception cref="FormatException">The record lies outside <paramref name="data"/>.</exception>
    public static ItemRecord DecodeItem(ReadOnlySpan<byte> data, int offset)
    {
        EnsureRange(data, offset, ItemStride, "Item record");
        return new ItemRecord(
            BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4)),
            ReadU16(data, offset + 4), ReadU16(data, offset + 6),
            data[offset + 8], data[offset + 9]);
    }

    /// <summary>
    /// Decodes one item effect record from <paramref name="data"/> at <paramref name="offset"/>.
    /// Layout: u8 flags, u8 kind, u16 amount (4 bytes total).
    /// </summary>
    /// <param name="data">Data span that contains the item effect record.</param>
    /// <param name="offset">Byte offset of the record within <paramref name="data"/>.</param>
    /// <returns>The decoded item effect record.</returns>
    /// <exception cref="FormatException">The record lies outside <paramref name="data"/>.</exception>
    public static ItemEffect DecodeItemEffect(ReadOnlySpan<byte> data, int offset)
    {
        EnsureRange(data, offset, ItemEffectStride, "Item effect");
        return new ItemEffect(data[offset], data[offset + 1], ReadU16(data, offset + 2));
    }

    /// <summary>
    /// Decodes one creature record from <paramref name="data"/> at <paramref name="offset"/>.
    /// The record is 0x58 bytes and packs the creature's combat stats, resistances, skills,
    /// growth rates and other battle attributes in a fixed layout.
    /// </summary>
    /// <param name="data">Data span that contains the creature record.</param>
    /// <param name="offset">Byte offset of the record within <paramref name="data"/>.</param>
    /// <returns>The decoded creature record with all battle attributes populated.</returns>
    /// <exception cref="FormatException">The record lies outside <paramref name="data"/>.</exception>
    public static DigimonRecord DecodeDigimon(ReadOnlySpan<byte> data, int offset)
    {
        EnsureRange(data, offset, DigimonStride, "Digimon record");
        return new DigimonRecord(
            ReadU16(data, offset),
            ReadU16Run(data, offset + 0x02, 6),
            ReadU16Run(data, offset + 0x0E, 7),
            ReadU16Run(data, offset + 0x1C, 7),
            ReadU16(data, offset + 0x2A),
            data.Slice(offset + 0x2C, 5).ToArray(),
            data.Slice(offset + 0x31, 6).ToArray(),
            data.Slice(offset + 0x37, 5).ToArray(),
            data[offset + 0x3C], data[offset + 0x3D], data[offset + 0x3E],
            data[offset + 0x3F], data[offset + 0x40], data[offset + 0x41], data[offset + 0x42],
            data.Slice(offset + 0x43, 6).ToArray(),
            data.Slice(offset + 0x49, 7).ToArray(),
            data.Slice(offset + 0x50, 5).ToArray(),
            data[offset + 0x55], data[offset + 0x56]);
    }

    /// <summary>
    /// Decodes one technique record from <paramref name="data"/> at <paramref name="offset"/>.
    /// The record is 0x12 bytes and packs the technique's MP cost, power, accuracy, element,
    /// effect and script attributes in a fixed layout.
    /// </summary>
    /// <param name="data">Data span that contains the technique record.</param>
    /// <param name="offset">Byte offset of the record within <paramref name="data"/>.</param>
    /// <returns>The decoded technique record.</returns>
    /// <exception cref="FormatException">The record lies outside <paramref name="data"/>.</exception>
    public static TechniqueRecord DecodeTechnique(ReadOnlySpan<byte> data, int offset)
    {
        EnsureRange(data, offset, TechniqueStride, "Technique record");
        return new TechniqueRecord(
            ReadU16(data, offset), ReadU16(data, offset + 2),
            data[offset + 4], data[offset + 5], data[offset + 6], data[offset + 7],
            data[offset + 8], data[offset + 9], data[offset + 10], data[offset + 11],
            data[offset + 12], data[offset + 13], data[offset + 14], data[offset + 15],
            data[offset + 16], data[offset + 17]);
    }

    /// <summary>
    /// Decodes one enemy battle configuration record from <paramref name="data"/> at <paramref name="offset"/>.
    /// The record is 0x46 bytes and contains the enemy's stat line, item drop, four tactic actions
    /// and a counter action.
    /// </summary>
    /// <param name="data">Data span that contains the battle table record.</param>
    /// <param name="offset">Byte offset of the record within <paramref name="data"/>.</param>
    /// <returns>The decoded battle table record.</returns>
    /// <exception cref="FormatException">The record lies outside <paramref name="data"/>.</exception>
    public static BattleTableRecord DecodeBattleTableRecord(ReadOnlySpan<byte> data, int offset)
    {
        EnsureRange(data, offset, BattleTableStride, "Battle table record");
        var actions = new BattleAction[4];
        for (int i = 0; i < actions.Length; i++)
        {
            actions[i] = DecodeBattleAction(data, offset + 0x32 + i * 4);
        }

        return new BattleTableRecord(
            ReadI16(data, offset), ReadI16(data, offset + 2), ReadI16(data, offset + 4), ReadI16(data, offset + 6),
            ReadI16Run(data, offset + 8, 3), ReadI16Run(data, offset + 0x0E, 5), ReadI16Run(data, offset + 0x18, 12),
            data[offset + 0x30], actions, DecodeBattleAction(data, offset + 0x42));
    }

    /// <summary>
    /// Decodes all enemy battle configuration records from <paramref name="data"/>; reads records
    /// until a zero-id terminator is found or the span is exhausted.
    /// </summary>
    /// <param name="data">Raw bytes of the battle table, starting at the first record.</param>
    /// <returns>All records before the zero-id sentinel, in table order.</returns>
    /// <exception cref="FormatException">The span is shorter than one record, or no zero-id sentinel is found.</exception>
    public static IReadOnlyList<BattleTableRecord> DecodeBattleTable(ReadOnlySpan<byte> data)
    {
        if (data.Length < BattleTableStride)
        {
            throw new FormatException("Battle table is shorter than one record.");
        }

        var records = new List<BattleTableRecord>();
        for (int offset = 0; offset + BattleTableStride <= data.Length; offset += BattleTableStride)
        {
            BattleTableRecord record = DecodeBattleTableRecord(data, offset);
            if (record.Id == 0)
            {
                return records;
            }

            records.Add(record);
        }

        throw new FormatException("Battle table has no zero-id terminator.");
    }

    /// <summary>
    /// Decodes one evolution prerequisite record from <paramref name="data"/> at <paramref name="offset"/>.
    /// Layout: s32 digimonIndex, s16 need0Index, s16 need0Level, s16 need1Index, s16 need1Level,
    /// s16 stat, s16 value (16 bytes). Creature indices are converted to disc ids via
    /// <paramref name="digimonIdsByIndex"/>.
    /// </summary>
    /// <param name="data">Data span that contains the evolution record.</param>
    /// <param name="offset">Byte offset of the record within <paramref name="data"/>.</param>
    /// <param name="digimonIdsByIndex">Zero-based list mapping one-based creature table positions to disc ids.</param>
    /// <returns>The decoded evolution record with ids resolved.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="digimonIdsByIndex"/> is null.</exception>
    /// <exception cref="FormatException">The record lies outside <paramref name="data"/> or a creature index is out of range.</exception>
    public static EvolutionRecord DecodeEvolution(ReadOnlySpan<byte> data, int offset, IReadOnlyList<int> digimonIdsByIndex)
    {
        ArgumentNullException.ThrowIfNull(digimonIdsByIndex);
        EnsureRange(data, offset, EvolutionStride, "Evolution record");
        int digimonIndex = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, 4));
        short need0Index = ReadI16(data, offset + 4);
        short need0Level = ReadI16(data, offset + 6);
        short need1Index = ReadI16(data, offset + 8);
        short need1Level = ReadI16(data, offset + 10);
        return new EvolutionRecord(digimonIndex, ResolveDigimonId(digimonIdsByIndex, digimonIndex),
        [new EvolutionNeed(need0Index, ResolveDigimonId(digimonIdsByIndex, need0Index), need0Level),
         new EvolutionNeed(need1Index, ResolveDigimonId(digimonIdsByIndex, need1Index), need1Level)],
        ReadI16(data, offset + 12), ReadI16(data, offset + 14));
    }

    /// <summary>
    /// Decodes one battle reward record from <paramref name="data"/> at <paramref name="offset"/>.
    /// Layout: s32 digimonExperience, s32 experience, s32 money (12 bytes total).
    /// </summary>
    /// <param name="data">Data span that contains the reward record.</param>
    /// <param name="offset">Byte offset of the record within <paramref name="data"/>.</param>
    /// <returns>The decoded battle reward.</returns>
    /// <exception cref="FormatException">The record lies outside <paramref name="data"/>.</exception>
    public static BattleReward DecodeReward(ReadOnlySpan<byte> data, int offset)
    {
        EnsureRange(data, offset, RewardStride, "Battle reward");
        return new BattleReward(
            BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset + 4, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset + 8, 4)));
    }

    /// <summary>
    /// Maps a one-based creature table index to its disc id in <paramref name="idsByIndex"/>.
    /// Index 0 is treated as "none" and returns 0 without a range check.
    /// </summary>
    /// <param name="idsByIndex">Zero-based list of disc ids indexed by one-based position.</param>
    /// <param name="oneBasedIndex">One-based creature table position, or 0 for "none".</param>
    /// <returns>The disc id, or 0 when <paramref name="oneBasedIndex"/> is 0.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="idsByIndex"/> is null.</exception>
    /// <exception cref="FormatException"><paramref name="oneBasedIndex"/> is outside the table bounds.</exception>
    public static int ResolveDigimonId(IReadOnlyList<int> idsByIndex, int oneBasedIndex)
    {
        ArgumentNullException.ThrowIfNull(idsByIndex);
        if (oneBasedIndex == 0)
        {
            return 0;
        }

        if (oneBasedIndex < 1 || oneBasedIndex > idsByIndex.Count)
        {
            throw new FormatException($"Evolution Digimon index {oneBasedIndex} is outside the creature table.");
        }

        return idsByIndex[oneBasedIndex - 1];
    }

    /// <summary>
    /// Decodes one 4-byte tactic action record: u8 target, u8 condition, s16 conditionArgument.
    /// </summary>
    /// <param name="data">Data span that contains the action record.</param>
    /// <param name="offset">Byte offset of the record within <paramref name="data"/>.</param>
    /// <returns>The decoded battle action.</returns>
    /// <exception cref="FormatException">The record lies outside <paramref name="data"/>.</exception>
    private static BattleAction DecodeBattleAction(ReadOnlySpan<byte> data, int offset)
    {
        EnsureRange(data, offset, 4, "Battle action");
        return new BattleAction(data[offset], data[offset + 1], ReadI16(data, offset + 2));
    }

    /// <summary>Reads a little-endian u16 from <paramref name="data"/> at <paramref name="offset"/>.</summary>
    /// <param name="data">Source data span.</param>
    /// <param name="offset">Byte offset; the read covers 2 bytes.</param>
    /// <returns>The decoded u16 value.</returns>
    private static ushort ReadU16(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));

    /// <summary>Reads a little-endian s16 from <paramref name="data"/> at <paramref name="offset"/>.</summary>
    /// <param name="data">Source data span.</param>
    /// <param name="offset">Byte offset; the read covers 2 bytes.</param>
    /// <returns>The decoded s16 value.</returns>
    private static short ReadI16(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadInt16LittleEndian(data.Slice(offset, 2));

    /// <summary>Reads <paramref name="count"/> consecutive little-endian s32 values from <paramref name="data"/> at <paramref name="offset"/>.</summary>
    /// <param name="data">Source data span.</param>
    /// <param name="offset">Byte offset of the first value; each element is 4 bytes.</param>
    /// <param name="count">Number of s32 values to read.</param>
    /// <returns>Array of decoded s32 values in source order.</returns>
    private static int[] ReadInt32Run(ReadOnlySpan<byte> data, int offset, int count)
    {
        var values = new int[count];
        for (int i = 0; i < count; i++)
        {
            values[i] = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset + i * sizeof(int), sizeof(int)));
        }

        return values;
    }

    /// <summary>Reads <paramref name="count"/> consecutive little-endian u16 values from <paramref name="data"/> at <paramref name="offset"/>.</summary>
    /// <param name="data">Source data span.</param>
    /// <param name="offset">Byte offset of the first value; each element is 2 bytes.</param>
    /// <param name="count">Number of u16 values to read.</param>
    /// <returns>Array of decoded u16 values in source order.</returns>
    private static ushort[] ReadU16Run(ReadOnlySpan<byte> data, int offset, int count)
    {
        var values = new ushort[count];
        for (int i = 0; i < count; i++)
        {
            values[i] = ReadU16(data, offset + i * 2);
        }

        return values;
    }

    /// <summary>Reads <paramref name="count"/> consecutive little-endian s16 values from <paramref name="data"/> at <paramref name="offset"/>.</summary>
    /// <param name="data">Source data span.</param>
    /// <param name="offset">Byte offset of the first value; each element is 2 bytes.</param>
    /// <param name="count">Number of s16 values to read.</param>
    /// <returns>Array of decoded s16 values in source order.</returns>
    private static short[] ReadI16Run(ReadOnlySpan<byte> data, int offset, int count)
    {
        var values = new short[count];
        for (int i = 0; i < count; i++)
        {
            values[i] = ReadI16(data, offset + i * 2);
        }

        return values;
    }

    /// <summary>Throws <see cref="FormatException"/> when the slice [<paramref name="offset"/>, <paramref name="offset"/>+<paramref name="length"/>) lies outside <paramref name="data"/>.</summary>
    /// <param name="data">The span being validated.</param>
    /// <param name="offset">Byte offset of the region to validate.</param>
    /// <param name="length">Byte length of the region to validate.</param>
    /// <param name="kind">Human-readable name of the structure, used in the error message.</param>
    /// <exception cref="FormatException">The region is out of bounds.</exception>
    private static void EnsureRange(ReadOnlySpan<byte> data, int offset, int length, string kind)
    {
        if (offset < 0 || length < 0 || offset > data.Length - length)
        {
            throw new FormatException($"{kind} lies outside the supplied buffer.");
        }
    }
}
