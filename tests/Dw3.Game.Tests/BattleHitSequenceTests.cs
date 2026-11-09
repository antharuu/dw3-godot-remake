using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests;

/// <summary>Verifies ordered multi-hit and counter reactions in the battle simulation.</summary>
public sealed class BattleHitSequenceTests
{
    /// <summary>Verifies that every rolled multi-hit is recorded in order, including a partial miss.</summary>
    [Fact]
    public void PlayerTechnique_MultiHitWithPartialMiss_RecordsEveryHitAndAppliesOnlyLandedDamage()
    {
        BattleState state = CreateState(enemyHp: 100);
        var technique = new TechData
        {
            Accuracy = 64,
            Effect = BattleTechniqueEffect.MultiHit,
            HitCount = 3,
            Power = 1,
        };

        Assert.True(state.PlayerTechnique(technique, new SequenceRng(0, 127, 0)));

        Assert.Equal([true, false, true], state.LastHitEvents.Select(hit => hit.Landed));
        Assert.Equal([0, 1, 2], state.LastHitEvents.Select(hit => hit.HitIndex));
        Assert.All(state.LastHitEvents, hit => Assert.Equal(BattleTechniqueEffect.MultiHit, hit.Effect));
        Assert.Equal(100 - state.LastHitEvents.Sum(hit => hit.Damage), state.Enemies[0]!.Hp);
    }

    /// <summary>Verifies that a sequence does not roll or apply hits after it ends the battle.</summary>
    [Fact]
    public void PlayerTechnique_MultiHitKnockout_StopsBeforeTheNextHit()
    {
        BattleState state = CreateState(enemyHp: 1);
        var technique = new TechData
        {
            Accuracy = 128,
            Effect = BattleTechniqueEffect.MultiHit,
            HitCount = 3,
            Power = 1,
        };

        Assert.True(state.PlayerTechnique(technique, new SequenceRng(0, 0, 0)));

        Assert.Single(state.LastHitEvents);
        Assert.Equal(BattleOutcome.Won, state.Outcome);
    }

    /// <summary>Verifies that a counter is one reaction, clears its charge and never advances the enemy turn.</summary>
    [Fact]
    public void PlayerAttack_CounterReaction_ClearsChargeWithoutCreatingAnEnemyTurn()
    {
        BattleState state = CreateState(enemyHp: 100);
        BattleFighter enemy = Assert.IsType<BattleFighter>(state.ActiveEnemyFighter);
        enemy.Charge = 64;
        enemy.Counter = new BattleTableAction(1, 3, 0);
        enemy.Techs = [77, 0, 0];
        state.Techs = new Dictionary<int, TechData>
        {
            [77] = new TechData { Accuracy = 128, EffectPower = 64 },
        };
        int playerHp = Assert.IsType<BattleFighter>(state.ActivePlayerFighter).Hp;

        Assert.True(state.PlayerAttack(new TechData { Accuracy = 128, Power = 1 }, new SequenceRng(0, 0)));

        Assert.Equal(0, enemy.Charge);
        Assert.Equal(0, enemy.Turns);
        Assert.Equal(2, state.LastHitEvents.Count);
        Assert.True(state.LastHitEvents[1].IsCounter);
        Assert.True(Assert.IsType<BattleFighter>(state.ActivePlayerFighter).Hp < playerHp);
    }

    /// <summary>Verifies that the triple-hit equipment modifier makes a plain technique produce three ordered events.</summary>
    [Fact]
    public void PlayerTechnique_TripleHitModifier_PlainTechnique_ReturnsThreeHitEvents()
    {
        BattleState state = CreateStateWithPlayerTripleHit(enemyHp: 100);
        var technique = new TechData
        {
            Accuracy = 128,
            Power = 1,
        };

        Assert.True(state.PlayerTechnique(technique, new SequenceRng(0)));

        Assert.Equal(3, state.LastHitEvents.Count);
        Assert.All(state.LastHitEvents, hit => Assert.False(hit.IsCounter));
        Assert.All(state.LastHitEvents, hit => Assert.True(hit.Landed));
    }

    /// <summary>Verifies that the triple-hit modifier does not apply to counter-attacks even when the counter attacker carries the modifier.</summary>
    [Fact]
    public void PlayerAttack_EnemyWithTripleHit_CounterReactionIsStillSingleHit()
    {
        BattleState state = CreateStateWithEnemyTripleHit(enemyHp: 100);
        BattleFighter enemy = Assert.IsType<BattleFighter>(state.ActiveEnemyFighter);
        enemy.Counter = new BattleTableAction(1, 3, 0);
        enemy.Techs = [77, 0, 0];
        state.Techs = new Dictionary<int, TechData>
        {
            [77] = new TechData { Accuracy = 128, EffectPower = 64 },
        };

        Assert.True(state.PlayerAttack(new TechData { Accuracy = 128, Power = 1 }, new SequenceRng(0, 0)));

        // 1 player hit + 1 counter — TripleHit on enemy stats does not expand the counter
        Assert.Equal(2, state.LastHitEvents.Count);
        Assert.True(state.LastHitEvents[1].IsCounter);
    }

    /// <summary>Verifies that Double Magic rolls accuracy from the primary entry but resolves its two hits with the distinct source damage entries.</summary>
    [Fact]
    public void PlayerTechnique_DoubleMagic_UsesBothRequiredDamageEntriesAndSpendsMpOnce()
    {
        BattleState state = CreateState(enemyHp: 100, firstElementResistance: 100, secondElementResistance: 300);
        BattleFighter player = Assert.IsType<BattleFighter>(state.ActivePlayerFighter);
        BattleFighter enemy = Assert.IsType<BattleFighter>(state.ActiveEnemyFighter);
        state.Techs = new Dictionary<int, TechData>
        {
            [BattleTechniqueEffect.DoubleMagicFirstDamageTechniqueId] = MagicDamageTechnique(element: 2),
            [BattleTechniqueEffect.DoubleMagicSecondDamageTechniqueId] = MagicDamageTechnique(element: 3),
        };
        var primary = new TechData
        {
            Accuracy = 128,
            Effect = BattleTechniqueEffect.DoubleMagic,
            Icon = BattleConst.TechMagic,
            Mp = 7,
            Power = 80,
        };

        Assert.True(state.PlayerTechnique(primary, new SequenceRng(0, 0)));

        Assert.Equal(93, player.Mp);
        Assert.Equal([10, 5], state.LastHitEvents.Select(hit => hit.Damage));
        Assert.Equal(85, enemy.Hp);
    }

    /// <summary>Verifies that Double Magic consumes one primary accuracy roll per hit and never applies alternate damage after a miss.</summary>
    [Fact]
    public void PlayerTechnique_DoubleMagic_MissUsesPrimaryAccuracyAndLeavesEnemyUnchanged()
    {
        BattleState state = CreateState(enemyHp: 100);
        state.Techs = DoubleMagicDamageTechniques();
        var primary = new TechData
        {
            Accuracy = 0,
            Effect = BattleTechniqueEffect.DoubleMagic,
            Icon = BattleConst.TechMagic,
            Mp = 7,
            Power = 80,
        };

        Assert.True(state.PlayerTechnique(primary, new SequenceRng(127, 127)));

        Assert.Equal([false, false], state.LastHitEvents.Select(hit => hit.Landed));
        Assert.All(state.LastHitEvents, hit => Assert.Equal(0, hit.Damage));
        Assert.Equal(100, Assert.IsType<BattleFighter>(state.ActiveEnemyFighter).Hp);
    }

    /// <summary>Verifies that the enemy path selects the same two source damage entries while retaining the primary technique's accuracy and one MP cost.</summary>
    [Fact]
    public void EnemyTechnique_DoubleMagic_UsesBothRequiredDamageEntriesAndSpendsMpOnce()
    {
        BattleState state = CreateState(enemyHp: 100, firstElementResistance: 100, secondElementResistance: 300);
        BattleFighter enemy = Assert.IsType<BattleFighter>(state.ActiveEnemyFighter);
        BattleFighter player = Assert.IsType<BattleFighter>(state.ActivePlayerFighter);
        state.Techs = DoubleMagicDamageTechniques();
        var primary = new TechData
        {
            Accuracy = 128,
            Effect = BattleTechniqueEffect.DoubleMagic,
            Icon = BattleConst.TechMagic,
            Mp = 7,
            Power = 80,
        };

        Assert.True(state.EnemyTechnique(primary, new SequenceRng(0, 0)));

        Assert.Equal(93, enemy.Mp);
        Assert.Equal([10, 5], state.LastHitEvents.Select(hit => hit.Damage));
        Assert.Equal(85, player.Hp);
    }

    /// <summary>Verifies that a lethal first Double Magic hit follows the existing sequence stop and does not roll the second hit.</summary>
    [Fact]
    public void PlayerTechnique_DoubleMagic_LethalFirstHitStopsBeforeSecondHit()
    {
        BattleState state = CreateState(enemyHp: 5);
        state.Techs = new Dictionary<int, TechData>
        {
            [BattleTechniqueEffect.DoubleMagicFirstDamageTechniqueId] = new TechData { Icon = BattleConst.TechMagic, Power = 100 },
            [BattleTechniqueEffect.DoubleMagicSecondDamageTechniqueId] = new TechData { Icon = BattleConst.TechMagic, Power = 100 },
        };
        var primary = new TechData { Accuracy = 128, Effect = BattleTechniqueEffect.DoubleMagic, Icon = BattleConst.TechMagic };

        Assert.True(state.PlayerTechnique(primary, new SequenceRng(0, 0)));

        Assert.Single(state.LastHitEvents);
        Assert.Equal(BattleOutcome.Won, state.Outcome);
    }

    /// <summary>Verifies that missing Double Magic damage data is rejected before MP, hits, or random input mutate the battle.</summary>
    [Fact]
    public void PlayerTechnique_DoubleMagicWithoutBothDamageEntries_IsRefusedBeforeResources()
    {
        BattleState state = CreateState(enemyHp: 100);
        BattleFighter player = Assert.IsType<BattleFighter>(state.ActivePlayerFighter);
        var rng = new CountingRng(0);
        state.Techs = new Dictionary<int, TechData>
        {
            [BattleTechniqueEffect.DoubleMagicFirstDamageTechniqueId] = MagicDamageTechnique(element: 2),
        };
        var primary = new TechData
        {
            Accuracy = 128,
            Effect = BattleTechniqueEffect.DoubleMagic,
            Icon = BattleConst.TechMagic,
            Mp = 7,
        };

        Assert.False(state.PlayerTechnique(primary, rng));

        Assert.Equal(100, player.Mp);
        Assert.Empty(state.LastHitEvents);
        Assert.Equal(0, rng.CallCount);
    }

    /// <summary>Verifies that the enemy also refuses incomplete Double Magic data before MP or random input mutate the battle.</summary>
    [Fact]
    public void EnemyTechnique_DoubleMagicWithoutBothDamageEntries_IsRefusedBeforeResources()
    {
        BattleState state = CreateState(enemyHp: 100);
        BattleFighter enemy = Assert.IsType<BattleFighter>(state.ActiveEnemyFighter);
        var rng = new CountingRng(0);
        state.Techs = new Dictionary<int, TechData>
        {
            [BattleTechniqueEffect.DoubleMagicSecondDamageTechniqueId] = MagicDamageTechnique(element: 3),
        };
        var primary = new TechData
        {
            Accuracy = 128,
            Effect = BattleTechniqueEffect.DoubleMagic,
            Icon = BattleConst.TechMagic,
            Mp = 9,
        };

        Assert.False(state.EnemyTechnique(primary, rng));

        Assert.Equal(100, enemy.Mp);
        Assert.Empty(state.LastHitEvents);
        Assert.Equal(0, rng.CallCount);
    }

    /// <summary>Creates the source-keyed pair of fabricated Double Magic damage records.</summary>
    private static Dictionary<int, TechData> DoubleMagicDamageTechniques() => new()
    {
        [BattleTechniqueEffect.DoubleMagicFirstDamageTechniqueId] = MagicDamageTechnique(element: 2),
        [BattleTechniqueEffect.DoubleMagicSecondDamageTechniqueId] = MagicDamageTechnique(element: 3),
    };

    /// <summary>Creates an alternate magic damage record whose element makes the selected source entry observable through resistance.</summary>
    private static TechData MagicDamageTechnique(int element) => new()
    {
        Element = element,
        Icon = BattleConst.TechMagic,
        Power = 10,
    };

    private static BattleState CreateState(int enemyHp, int firstElementResistance = 100, int secondElementResistance = 100)
    {
        var stats = new BattleStats { Level = 10 };
        stats.Stats[BattleStat.Attack] = 20;
        stats.Stats[BattleStat.Defense] = 20;
        stats.Stats[BattleStat.Spirit] = 20;
        stats.Stats[BattleStat.Speed] = 100;
        stats.Resist[0] = firstElementResistance;
        stats.Resist[1] = secondElementResistance;
        var setup = new BattleSetup();
        setup.Partners[0] = new PartnerSeed { Id = 1, Hp = 100, MaxHp = 100, Mp = 100, MaxMp = 100, Stats = stats };
        setup.Enemies[0] = new BattleEnemy { Fighter = 2, Hp = enemyHp, Mp = 100, Strength = 16, BaseStats = stats };
        return BattleState.InitFighters(setup, digimon: 1, new SequenceRng(127));
    }

    private static BattleState CreateStateWithPlayerTripleHit(int enemyHp)
    {
        var playerStats = new BattleStats { Level = 10, TripleHit = 1 };
        playerStats.Stats[BattleStat.Attack] = 20;
        playerStats.Stats[BattleStat.Defense] = 20;
        playerStats.Stats[BattleStat.Speed] = 100;
        var enemyStats = new BattleStats { Level = 10 };
        enemyStats.Stats[BattleStat.Attack] = 20;
        enemyStats.Stats[BattleStat.Defense] = 1;
        enemyStats.Stats[BattleStat.Speed] = 100;
        var setup = new BattleSetup();
        setup.Partners[0] = new PartnerSeed { Id = 1, Hp = 100, MaxHp = 100, Mp = 100, MaxMp = 100, Stats = playerStats };
        setup.Enemies[0] = new BattleEnemy { Fighter = 2, Hp = enemyHp, Mp = 100, Strength = 16, BaseStats = enemyStats };
        return BattleState.InitFighters(setup, digimon: 1, new SequenceRng(127));
    }

    private static BattleState CreateStateWithEnemyTripleHit(int enemyHp)
    {
        var playerStats = new BattleStats { Level = 10 };
        playerStats.Stats[BattleStat.Attack] = 20;
        playerStats.Stats[BattleStat.Defense] = 20;
        playerStats.Stats[BattleStat.Speed] = 100;
        var enemyStats = new BattleStats { Level = 10, TripleHit = 1 };
        enemyStats.Stats[BattleStat.Attack] = 20;
        enemyStats.Stats[BattleStat.Defense] = 20;
        enemyStats.Stats[BattleStat.Speed] = 100;
        var setup = new BattleSetup();
        setup.Partners[0] = new PartnerSeed { Id = 1, Hp = 100, MaxHp = 100, Mp = 100, MaxMp = 100, Stats = playerStats };
        setup.Enemies[0] = new BattleEnemy { Fighter = 2, Hp = enemyHp, Mp = 100, Strength = 16, BaseStats = enemyStats };
        return BattleState.InitFighters(setup, digimon: 1, new SequenceRng(127));
    }

    private sealed class SequenceRng(params int[] values) : IBattleRng
    {
        private readonly Queue<int> _values = new(values);

        public int NextValue() => _values.Count > 0 ? _values.Dequeue() : 0;
    }

    private sealed class CountingRng(params int[] values) : IBattleRng
    {
        private readonly Queue<int> _values = new(values);

        public int CallCount { get; private set; }

        public int NextValue()
        {
            CallCount++;
            return _values.Count > 0 ? _values.Dequeue() : 0;
        }
    }
}
