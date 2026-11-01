using System.Buffers.Binary;
using Dw3.Formats.Battle;
using Xunit;

namespace Dw3.Formats.Tests.Battle;

public sealed class BattleDataDecoderTests
{
    [Fact]
    public void DecodeDigimon_ReadsTheCompleteFixedRecord()
    {
        byte[] bytes = new byte[BattleDataDecoder.DigimonStride];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, 383);
        for (int i = 0; i < 6; i++)
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2 + i * 2), (ushort)(10 + i));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(0x2A), 77);
        bytes[0x3C] = 9;
        bytes[0x55] = 21;
        bytes[0x56] = 4;

        DigimonRecord record = BattleDataDecoder.DecodeDigimon(bytes, 0);

        Assert.Equal(383, record.Id);
        Assert.Equal([10, 11, 12, 13, 14, 15], record.BattleStats);
        Assert.Equal(77, record.PairTechnique);
        Assert.Equal(9, record.ExperienceLevel);
        Assert.Equal(21, record.NameId);
        Assert.Equal(4, record.Family);
    }

    [Fact]
    public void DecodeTechnique_ReadsEveryScalarField()
    {
        byte[] bytes = new byte[BattleDataDecoder.TechniqueStride];
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, 19);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2), 88);
        for (int i = 0; i < 14; i++) bytes[4 + i] = (byte)(i + 1);

        TechniqueRecord record = BattleDataDecoder.DecodeTechnique(bytes, 0);

        Assert.Equal(19, record.Mp);
        Assert.Equal(88, record.Power);
        Assert.Equal(1, record.Icon);
        Assert.Equal(14, record.HitCount);
    }

    [Fact]
    public void DecodeBattleTable_StopsAtZeroId()
    {
        byte[] bytes = new byte[BattleDataDecoder.BattleTableStride * 2];
        BinaryPrimitives.WriteInt16LittleEndian(bytes, 12);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(BattleDataDecoder.BattleTableStride), 0);

        IReadOnlyList<BattleTableRecord> records = BattleDataDecoder.DecodeBattleTable(bytes);

        Assert.Single(records);
        Assert.Equal(12, records[0].Id);
    }

    [Fact]
    public void DecodeBattleTable_RejectsMissingTerminator()
    {
        byte[] bytes = new byte[BattleDataDecoder.BattleTableStride];
        BinaryPrimitives.WriteInt16LittleEndian(bytes, 1);

        Assert.Throws<FormatException>(() => BattleDataDecoder.DecodeBattleTable(bytes));
    }

    [Fact]
    public void DecodeEvolution_ResolvesOneBasedDigimonIndexes()
    {
        byte[] bytes = new byte[BattleDataDecoder.EvolutionStride];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, 2);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(4), 1);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(6), 5);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(12), 7);
        BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(14), 20);

        EvolutionRecord record = BattleDataDecoder.DecodeEvolution(bytes, 0, [383, 385]);

        Assert.Equal(385, record.DigimonId);
        Assert.Equal(383, record.Needs[0].DigimonId);
        Assert.Equal(5, record.Needs[0].Level);
        Assert.Equal(7, record.Stat);
        Assert.Equal(20, record.Value);
    }

    [Fact]
    public void DecodeReward_ReadsSignedValuesAndRejectsTruncation()
    {
        byte[] bytes = new byte[BattleDataDecoder.RewardStride];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), -4);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), 50);

        BattleReward record = BattleDataDecoder.DecodeReward(bytes, 0);

        Assert.Equal(1, record.DigimonExperience);
        Assert.Equal(-4, record.Experience);
        Assert.Equal(50, record.Money);
        Assert.Throws<FormatException>(() => BattleDataDecoder.DecodeReward(bytes.AsSpan()[..11], 0));
    }

    [Fact]
    public void DecodeGrowth_ReadsSignedTablesAndRejectsTruncation()
    {
        byte[] bytes = new byte[316];
        for (int index = 0; index < 79; index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(index * 4), index - 4);
        }

        BattleGrowthData growth = BattleDataDecoder.DecodeGrowth(bytes, 0);

        Assert.Equal([-4, -3, -2, -1], growth.LevelExperience);
        Assert.Equal([0, 1, 2, 3], growth.VitalCuts);
        Assert.Equal([4, 5, 6, 7, 8, 9, 10, 11, 12], growth.VitalRandom);
        Assert.Equal([13, 14, 15, 16, 17, 18, 19, 20, 21], growth.StatGains[0]);
        Assert.Equal([67, 68, 69, 70, 71, 72, 73, 74], growth.ResistanceGains);
        Assert.Throws<FormatException>(() => BattleDataDecoder.DecodeGrowth(bytes.AsSpan()[..315], 0));
    }

    [Fact]
    public void DecodeItemAndEffect_ReadsFixedExecutableRecords()
    {
        byte[] bytes = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 0x80041234);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), 120);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(6), 60);
        bytes[8] = 2;
        bytes[9] = 25;
        bytes[12] = 3;
        bytes[13] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(14), 500);

        ItemRecord item = BattleDataDecoder.DecodeItem(bytes, 0);
        ItemEffect effect = BattleDataDecoder.DecodeItemEffect(bytes, 12);

        Assert.Equal(0x80041234u, item.DataPointer);
        Assert.Equal(120, item.Price);
        Assert.Equal(60, item.SellPrice);
        Assert.Equal(2, item.Kind);
        Assert.Equal(25, item.Type);
        Assert.Equal(3, effect.Flags);
        Assert.Equal(1, effect.Kind);
        Assert.Equal(500, effect.Amount);
        Assert.Throws<FormatException>(() => BattleDataDecoder.DecodeItem(bytes.AsSpan()[..11], 0));
    }

    [Fact]
    public void BattleJsonWriter_EmitsCompleteDeterministicBattleAndDigivolutionFacts()
    {
        var digimon = new DigimonRecord(383, [1], [2], [3], 4, [5], [6], [7], 8, 9, 10, 11, 12, 13, 14, [15], [16], [17], 18, 19);
        var technique = new TechniqueRecord(20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35);
        var action = new BattleAction(36, 37, 38);
        var enemy = new BattleTableRecord(39, 40, 41, 42, [43], [44], [45], 46, [action, action, action, action], action);
        var item = new ItemRecord(0x80041234, 47, 48, 49, 50);
        var effect = new ItemEffect(51, 52, 53);
        var reward = new BattleReward(54, 55, 56);
        var growth = new BattleGrowthData([57], [58], [59], [[60]], [61]);
        var evolution = new EvolutionRecord(1, 383, [new EvolutionNeed(0, 0, 0), new EvolutionNeed(1, 383, 62)], 63, 64);

        string battleA = BattleJsonWriter.WriteBattle([digimon], [technique], [enemy], [(item, effect)], [reward], growth);
        string battleB = BattleJsonWriter.WriteBattle([digimon], [technique], [enemy], [(item, effect)], [reward], growth);
        string digivolution = BattleJsonWriter.WriteDigivolution([digimon], [[evolution]]);

        Assert.Equal(battleA, battleB);
        Assert.Contains("\"digimon\"", battleA);
        Assert.Contains("\"items\"", battleA);
        Assert.Contains("\"rewards\"", battleA);
        Assert.Contains("\"growth\"", battleA);
        Assert.Contains("\"conditionArgument\": 38", battleA);
        Assert.Contains("\"needs\"", digivolution);
        Assert.Contains("\"digimonId\": 383", digivolution);
    }
}
