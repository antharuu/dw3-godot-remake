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

    private static BattleState CreateState(int enemyHp)
    {
        var stats = new BattleStats { Level = 10 };
        stats.Stats[BattleStat.Attack] = 20;
        stats.Stats[BattleStat.Defense] = 20;
        stats.Stats[BattleStat.Speed] = 100;
        var setup = new BattleSetup();
        setup.Partners[0] = new PartnerSeed { Id = 1, Hp = 100, MaxHp = 100, Mp = 100, MaxMp = 100, Stats = stats };
        setup.Enemies[0] = new BattleEnemy { Fighter = 2, Hp = enemyHp, Mp = 100, Strength = 16, BaseStats = stats };
        return BattleState.InitFighters(setup, digimon: 1, new SequenceRng(127));
    }

    private sealed class SequenceRng(params int[] values) : IBattleRng
    {
        private readonly Queue<int> _values = new(values);

        public int NextValue() => _values.Count > 0 ? _values.Dequeue() : 0;
    }
}
