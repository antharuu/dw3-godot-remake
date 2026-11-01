using System.Buffers.Binary;

namespace Dw3.Formats.Battle;

/// <summary>
/// Reads fixed-layout battle records. The addresses that locate these records are intentionally
/// owned by the extraction step. This type only validates and decodes byte structures.
/// </summary>
public static class BattleDataDecoder
{
    public const int DigimonStride = 0x58;
    public const int TechniqueStride = 0x12;
    public const int BattleTableStride = 0x46;
    public const int EvolutionStride = 16;
    public const int RewardStride = 12;
    public const int ItemStride = 12;
    public const int ItemEffectStride = 4;

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

    public static ItemRecord DecodeItem(ReadOnlySpan<byte> data, int offset)
    {
        EnsureRange(data, offset, ItemStride, "Item record");
        return new ItemRecord(
            BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4)),
            ReadU16(data, offset + 4), ReadU16(data, offset + 6),
            data[offset + 8], data[offset + 9]);
    }

    public static ItemEffect DecodeItemEffect(ReadOnlySpan<byte> data, int offset)
    {
        EnsureRange(data, offset, ItemEffectStride, "Item effect");
        return new ItemEffect(data[offset], data[offset + 1], ReadU16(data, offset + 2));
    }

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

    public static BattleReward DecodeReward(ReadOnlySpan<byte> data, int offset)
    {
        EnsureRange(data, offset, RewardStride, "Battle reward");
        return new BattleReward(
            BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset + 4, 4)),
            BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset + 8, 4)));
    }

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

    private static BattleAction DecodeBattleAction(ReadOnlySpan<byte> data, int offset)
    {
        EnsureRange(data, offset, 4, "Battle action");
        return new BattleAction(data[offset], data[offset + 1], ReadI16(data, offset + 2));
    }

    private static ushort ReadU16(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));
    private static short ReadI16(ReadOnlySpan<byte> data, int offset) => BinaryPrimitives.ReadInt16LittleEndian(data.Slice(offset, 2));
    private static int[] ReadInt32Run(ReadOnlySpan<byte> data, int offset, int count)
    {
        var values = new int[count];
        for (int i = 0; i < count; i++)
        {
            values[i] = BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset + i * sizeof(int), sizeof(int)));
        }

        return values;
    }

    private static ushort[] ReadU16Run(ReadOnlySpan<byte> data, int offset, int count)
    {
        var values = new ushort[count];
        for (int i = 0; i < count; i++)
        {
            values[i] = ReadU16(data, offset + i * 2);
        }

        return values;
    }

    private static short[] ReadI16Run(ReadOnlySpan<byte> data, int offset, int count)
    {
        var values = new short[count];
        for (int i = 0; i < count; i++)
        {
            values[i] = ReadI16(data, offset + i * 2);
        }

        return values;
    }

    private static void EnsureRange(ReadOnlySpan<byte> data, int offset, int length, string kind)
    {
        if (offset < 0 || length < 0 || offset > data.Length - length)
        {
            throw new FormatException($"{kind} lies outside the supplied buffer.");
        }
    }
}
