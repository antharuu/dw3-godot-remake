using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests;

/// <summary>Verifies that damaging techniques apply their supported status families through the real battle action path.</summary>
public sealed class BattleTechniqueEffectTests
{
    /// <summary>Verifies that a landed poison technique uses its extracted chance and power exactly once.</summary>
    [Fact]
    public void PlayerTechnique_LandedPoisonEffect_InflictsPoisonWithTechniqueFields()
    {
        BattleState state = CreateState();
        var technique = new TechData
        {
            Accuracy = 128,
            Effect = BattleTechniqueEffect.Poison,
            EffectChance = 127,
            EffectPower = 20,
            Power = 1,
        };

        Assert.True(state.PlayerTechnique(technique, new SequenceRng(0, 0)));

        Assert.NotEqual(0, Assert.IsType<BattleFighter>(state.ActiveEnemyFighter).Flags & FighterFlags.Poisoned);
    }

    /// <summary>Verifies that each supported control effect uses its own status rule through the technique path.</summary>
    [Theory]
    [InlineData(BattleTechniqueEffect.Paralysis, FighterFlags.Paralyzed)]
    [InlineData(BattleTechniqueEffect.Confusion, FighterFlags.Confused)]
    [InlineData(BattleTechniqueEffect.Sleep, FighterFlags.Asleep)]
    public void PlayerTechnique_LandedControlEffect_InflictsItsMappedStatus(int effect, int flag)
    {
        BattleState state = CreateState();
        var technique = new TechData
        {
            Accuracy = 128,
            Effect = effect,
            EffectChance = 127,
            EffectPower = 20,
            Power = 1,
        };

        Assert.True(state.PlayerTechnique(technique, new SequenceRng(0, 0, 0)));

        Assert.NotEqual(0, Assert.IsType<BattleFighter>(state.ActiveEnemyFighter).Flags & flag);
    }

    /// <summary>Verifies that an enemy technique uses the same supported control mapping against the active player.</summary>
    [Fact]
    public void EnemyTechnique_LandedSleepEffect_InflictsSleepOnPlayer()
    {
        BattleState state = CreateState();
        var technique = new TechData
        {
            Accuracy = 128,
            Effect = BattleTechniqueEffect.Sleep,
            EffectChance = 127,
            EffectPower = 20,
            Power = 1,
        };

        Assert.True(state.EnemyTechnique(technique, new SequenceRng(0, 0, 0)));

        Assert.NotEqual(0, Assert.IsType<BattleFighter>(state.ActivePlayerFighter).Flags & FighterFlags.Asleep);
    }

    /// <summary>Verifies that a missed status technique never retries its status roll or leaves a status behind.</summary>
    [Fact]
    public void PlayerTechnique_MissedPoisonEffect_DoesNotApplyOrRetryPoison()
    {
        BattleState state = CreateState();
        var technique = new TechData
        {
            Accuracy = 0,
            Effect = BattleTechniqueEffect.Poison,
            EffectChance = 127,
            EffectPower = 20,
            Power = 1,
        };

        Assert.True(state.PlayerTechnique(technique, new SequenceRng(127, 0)));

        Assert.Equal(0, Assert.IsType<BattleFighter>(state.ActiveEnemyFighter).Flags & FighterFlags.Poisoned);
    }

    private static BattleState CreateState()
    {
        var stats = new BattleStats();
        stats.Stats[BattleStat.Attack] = 20;
        stats.Stats[BattleStat.Defense] = 20;
        stats.Stats[BattleStat.Speed] = 100;
        var setup = new BattleSetup();
        setup.Partners[0] = new PartnerSeed { Id = 1, Hp = 100, MaxHp = 100, Mp = 100, MaxMp = 100, Stats = stats };
        setup.Enemies[0] = new BattleEnemy { Fighter = 2, Hp = 100, Mp = 100, Strength = 16, BaseStats = stats };
        return BattleState.InitFighters(setup, digimon: 1, new SequenceRng(127));
    }

    private sealed class SequenceRng(params int[] values) : IBattleRng
    {
        private readonly Queue<int> _values = new(values);

        public int NextValue() => _values.Count > 0 ? _values.Dequeue() : 0;
    }
}
