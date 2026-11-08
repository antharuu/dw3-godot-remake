using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests;

/// <summary>Verifies the poison status contract independently of rendering and action scripts.</summary>
public sealed class BattlePoisonStatusTests
{
    /// <summary>Verifies that a successful poison roll marks the active target and deals periodic damage once.</summary>
    [Fact]
    public void InflictPoison_AppliesToActiveTarget_AndTicksOnceWhenDue()
    {
        BattleState state = CreateState();

        Assert.True(state.InflictPoison(BattleSide.Player, chance: 127, power: 20, new SequenceRng(0)));
        AdvanceOneRound(state);

        BattleFighter enemy = Assert.IsType<BattleFighter>(state.ActiveEnemyFighter);
        Assert.True((enemy.Flags & FighterFlags.Poisoned) != 0);
        Assert.Equal(90, enemy.Hp);
    }

    /// <summary>Verifies that blocked and resisted poison create neither a flag nor a periodic event.</summary>
    [Fact]
    public void InflictPoison_RejectsBlockedAndResistedApplications()
    {
        BattleState blocked = CreateState();
        blocked.Blocks[BattleConst.BlockPoison] = 1;

        Assert.False(blocked.InflictPoison(BattleSide.Player, chance: 127, power: 20, new SequenceRng(0)));
        Assert.Equal(100, Assert.IsType<BattleFighter>(blocked.ActiveEnemyFighter).Hp);

        BattleState resisted = CreateState(enemyPoisonResistance: 200);

        Assert.False(resisted.InflictPoison(BattleSide.Player, chance: 127, power: 20, new SequenceRng(127)));
        AdvanceOneRound(resisted);
        Assert.Equal(100, Assert.IsType<BattleFighter>(resisted.ActiveEnemyFighter).Hp);
    }

    /// <summary>Verifies that refreshing poison replaces its pending damage rather than creating two ticks.</summary>
    [Fact]
    public void InflictPoison_RefreshesOnePeriodicEvent()
    {
        BattleState state = CreateState();

        Assert.True(state.InflictPoison(BattleSide.Player, chance: 127, power: 20, new SequenceRng(0)));
        Assert.True(state.InflictPoison(BattleSide.Player, chance: 127, power: 40, new SequenceRng(0)));
        AdvanceOneRound(state);

        Assert.Equal(80, Assert.IsType<BattleFighter>(state.ActiveEnemyFighter).Hp);
    }

    /// <summary>Verifies that curing poison removes both the persistent flag and the periodic event.</summary>
    [Fact]
    public void CurePoison_RemovesStatusAndPreventsFutureTicks()
    {
        BattleState state = CreateState();
        Assert.True(state.InflictPoison(BattleSide.Player, chance: 127, power: 20, new SequenceRng(0)));

        state.CurePoison(BattleSide.Enemy, fighterSlot: 0);
        AdvanceOneRound(state);

        BattleFighter enemy = Assert.IsType<BattleFighter>(state.ActiveEnemyFighter);
        Assert.Equal(0, enemy.Flags & FighterFlags.Poisoned);
        Assert.Equal(100, enemy.Hp);
    }

    /// <summary>Verifies that a poison KO clears poison and does not leave a second periodic event armed.</summary>
    [Fact]
    public void PoisonTick_KnocksOutTarget_AndDoesNotTickAgain()
    {
        BattleState state = CreateState(enemyHp: 5);
        Assert.True(state.InflictPoison(BattleSide.Player, chance: 127, power: 20, new SequenceRng(0)));

        for (var round = 0; round < 6; round++)
        {
            AdvanceOneRound(state);
        }

        BattleFighter enemy = Assert.IsType<BattleFighter>(state.Enemies[0]);
        Assert.Equal(0, enemy.Hp);
        Assert.Equal(0, enemy.Flags & FighterFlags.Poisoned);
        Assert.Equal(BattleOutcome.Won, state.Outcome);
    }

    private static void AdvanceOneRound(BattleState state)
    {
        Assert.True(state.PlayerSwitch(state.ActivePlayer == 0 ? 1 : 0));
        state.EnemyAttack(new TechData { Power = 0 }, new SequenceRng());
    }

    private static BattleState CreateState(int enemyHp = 100, int enemyPoisonResistance = 0)
    {
        var setup = new BattleSetup();
        setup.Partners[0] = Partner(id: 1, hp: 100);
        setup.Partners[1] = Partner(id: 2, hp: 100);
        setup.Enemies[0] = Enemy(id: 3, hp: enemyHp, poisonResistance: enemyPoisonResistance);
        return BattleState.InitFighters(setup, digimon: 1, new SequenceRng(127));
    }

    private static PartnerSeed Partner(int id, int hp) => new()
    {
        Id = id,
        Hp = hp,
        MaxHp = hp,
        Mp = 0,
        MaxMp = 0,
        Stats = Stats(),
    };

    private static BattleEnemy Enemy(int id, int hp, int poisonResistance) => new()
    {
        Fighter = id,
        Hp = hp,
        Mp = 0,
        Strength = 16,
        BaseStats = Stats(poisonResistance),
    };

    private static BattleStats Stats(int poisonResistance = 0)
    {
        var stats = new BattleStats();
        stats.Stats[BattleStat.Speed] = 100;
        stats.Resist[Resist.Poison] = poisonResistance;
        return stats;
    }

    private sealed class SequenceRng(params int[] values) : IBattleRng
    {
        private readonly Queue<int> _values = new(values);

        public int NextValue() => _values.Count > 0 ? _values.Dequeue() : 0;
    }
}
