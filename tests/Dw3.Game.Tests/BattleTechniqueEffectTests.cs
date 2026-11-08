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

    /// <summary>
    /// Verifies that a player technique whose integer damage rounds to zero (attacker Attack=20,
    /// defender Defense=200, Power=1 → 1×20/200=0) still applies its status effect because
    /// the hit landed; the gate must be on Landed, not on positive aggregate damage.
    /// </summary>
    [Fact]
    public void PlayerTechnique_LandedZeroDamagePoisonTechnique_AppliesPoisonRegardlessOfDamage()
    {
        BattleState state = CreateStateHighEnemyDefense();
        var technique = new TechData
        {
            Accuracy = 128,
            Effect = BattleTechniqueEffect.Poison,
            EffectChance = 127,
            EffectPower = 20,
            Power = 1,
        };

        Assert.True(state.PlayerTechnique(technique, new SequenceRng(0, 0)));

        var enemy = Assert.IsType<BattleFighter>(state.ActiveEnemyFighter);
        Assert.Equal(100, enemy.Hp);
        Assert.True(state.LastHitEvents.Count == 1 && state.LastHitEvents[0].Landed, "hit must have landed");
        Assert.Equal(0, state.LastHitEvents[0].Damage);
        Assert.NotEqual(0, enemy.Flags & FighterFlags.Poisoned);
    }

    /// <summary>
    /// Verifies that an enemy technique whose integer damage rounds to zero (enemy Attack=5 after
    /// strength scaling, player Defense=200, Power=1 → 1×1×5/200=0) still applies its status
    /// effect because the hit landed; the gate must be on Landed, not on positive aggregate damage.
    /// </summary>
    [Fact]
    public void EnemyTechnique_LandedZeroDamageSleepTechnique_AppliesSleepRegardlessOfDamage()
    {
        BattleState state = CreateStateHighPlayerDefense();
        var technique = new TechData
        {
            Accuracy = 128,
            Effect = BattleTechniqueEffect.Sleep,
            EffectChance = 127,
            EffectPower = 20,
            Power = 1,
        };

        Assert.True(state.EnemyTechnique(technique, new SequenceRng(0, 0, 0)));

        var player = Assert.IsType<BattleFighter>(state.ActivePlayerFighter);
        Assert.Equal(100, player.Hp);
        Assert.True(state.LastHitEvents.Count == 1 && state.LastHitEvents[0].Landed, "hit must have landed");
        Assert.Equal(0, state.LastHitEvents[0].Damage);
        Assert.NotEqual(0, player.Flags & FighterFlags.Asleep);
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

    /// <summary>Player Attack=20, enemy Defense=200 — technique with Power=1 lands but deals 1×20/200=0 integer damage.</summary>
    private static BattleState CreateStateHighEnemyDefense()
    {
        var playerStats = new BattleStats();
        playerStats.Stats[BattleStat.Attack] = 20;
        playerStats.Stats[BattleStat.Defense] = 20;
        playerStats.Stats[BattleStat.Speed] = 100;
        var enemyStats = new BattleStats();
        enemyStats.Stats[BattleStat.Attack] = 20;
        enemyStats.Stats[BattleStat.Defense] = 200;
        enemyStats.Stats[BattleStat.Speed] = 100;
        var setup = new BattleSetup();
        setup.Partners[0] = new PartnerSeed { Id = 1, Hp = 100, MaxHp = 100, Mp = 100, MaxMp = 100, Stats = playerStats };
        setup.Enemies[0] = new BattleEnemy { Fighter = 2, Hp = 100, Mp = 100, Strength = 16, BaseStats = enemyStats };
        return BattleState.InitFighters(setup, digimon: 1, new SequenceRng(127));
    }

    /// <summary>Player Defense=200, enemy BaseStats Attack=5 (Strength=16 → resolved 5×16/16=5) — technique Power=1 lands but deals 1×1×5/200=0 integer damage.</summary>
    private static BattleState CreateStateHighPlayerDefense()
    {
        var playerStats = new BattleStats();
        playerStats.Stats[BattleStat.Attack] = 20;
        playerStats.Stats[BattleStat.Defense] = 200;
        playerStats.Stats[BattleStat.Speed] = 100;
        var enemyStats = new BattleStats();
        enemyStats.Stats[BattleStat.Attack] = 5;
        enemyStats.Stats[BattleStat.Defense] = 20;
        enemyStats.Stats[BattleStat.Speed] = 100;
        var setup = new BattleSetup();
        setup.Partners[0] = new PartnerSeed { Id = 1, Hp = 100, MaxHp = 100, Mp = 100, MaxMp = 100, Stats = playerStats };
        setup.Enemies[0] = new BattleEnemy { Fighter = 2, Hp = 100, Mp = 100, Strength = 16, BaseStats = enemyStats };
        return BattleState.InitFighters(setup, digimon: 1, new SequenceRng(127));
    }

    private sealed class SequenceRng(params int[] values) : IBattleRng
    {
        private readonly Queue<int> _values = new(values);

        public int NextValue() => _values.Count > 0 ? _values.Dequeue() : 0;
    }
}
