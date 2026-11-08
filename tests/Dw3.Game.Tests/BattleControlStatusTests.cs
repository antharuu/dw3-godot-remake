using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests;

/// <summary>Verifies sleep, paralysis and confusion at their simulation and menu seams.</summary>
public sealed class BattleControlStatusTests
{
    /// <summary>Verifies that each control status stores its source strength and can be explicitly cured.</summary>
    [Fact]
    public void InflictControlStatus_StoresStrength_AndCureRemovesIt()
    {
        BattleState state = CreateState();

        Assert.True(state.InflictParalysis(BattleSide.Player, chance: 127, strength: 40, new SequenceRng(0, 0)));
        Assert.True(state.InflictConfusion(BattleSide.Player, chance: 127, strength: 48, new SequenceRng(0, 0)));
        Assert.True(state.InflictSleep(BattleSide.Player, chance: 127, strength: 56, new SequenceRng(0, 0)));

        BattleFighter enemy = Assert.IsType<BattleFighter>(state.ActiveEnemyFighter);
        Assert.Equal(40, enemy.Paralysis);
        Assert.Equal(48, enemy.Confusion);
        Assert.Equal(56, enemy.Sleep);
        Assert.Equal(FighterFlags.Paralyzed | FighterFlags.Confused | FighterFlags.Asleep, enemy.Flags & 0x0E);

        state.CureControlStatuses(BattleSide.Enemy, fighterSlot: 0);

        Assert.Equal(0, enemy.Paralysis);
        Assert.Equal(0, enemy.Confusion);
        Assert.Equal(0, enemy.Sleep);
        Assert.Equal(0, enemy.Flags & 0x0E);
    }

    /// <summary>Verifies that disabled sleeping commands are refused before they reach the simulation.</summary>
    [Fact]
    public void Choose_SleepingFighter_RejectsGreyedAttackTechniqueAndDigivolve()
    {
        var flow = new BattleFlowController();
        flow.Start(CreateState());
        BattleFighter player = Assert.IsType<BattleFighter>(flow.State.ActivePlayerFighter);
        player.Flags |= FighterFlags.Asleep;
        int enemyHp = Assert.IsType<BattleFighter>(flow.State.ActiveEnemyFighter).Hp;

        Assert.All(flow.RootMenuLines().Take(3), line => Assert.False(line.Enabled));
        Assert.False(flow.RootMenuLines()[(int)BattleCommand.Flee].Enabled);
        Assert.False(flow.Choose(BattleCommand.Attack, 0, new SequenceRng()));
        Assert.False(flow.Choose(BattleCommand.Technique, 99, new SequenceRng()));
        Assert.False(flow.Choose(BattleCommand.Digivolve, 2, new SequenceRng()));
        Assert.False(flow.Choose(BattleCommand.Flee, 0, new SequenceRng()));
        Assert.Equal(enemyHp, Assert.IsType<BattleFighter>(flow.State.ActiveEnemyFighter).Hp);
        Assert.Equal(BattleFlowPhase.PlayerCommand, flow.Phase);
    }

    /// <summary>Verifies that confusion exposes only the forced attack and rejects every other API command.</summary>
    [Fact]
    public void Choose_ConfusedFighter_OnlyAllowsForcedAttack()
    {
        var flow = new BattleFlowController();
        flow.Start(CreateState());
        Assert.IsType<BattleFighter>(flow.State.ActivePlayerFighter).Flags |= FighterFlags.Confused;

        IReadOnlyList<BattleMenuLine> lines = flow.RootMenuLines();
        Assert.True(lines[(int)BattleCommand.Attack].Enabled);
        Assert.All(lines.Skip(1), line => Assert.False(line.Enabled));
        Assert.False(flow.Choose(BattleCommand.Item, 99, new SequenceRng()));
    }

    /// <summary>Verifies that a successful paralysis check spends the player turn without executing the selected attack.</summary>
    [Fact]
    public void Choose_ParalyzedFighter_LosesTurnWithoutExecutingAttack()
    {
        var flow = new BattleFlowController();
        flow.Start(CreateState());
        BattleFighter player = Assert.IsType<BattleFighter>(flow.State.ActivePlayerFighter);
        player.Flags |= FighterFlags.Paralyzed;
        player.Paralysis = 32;
        int enemyHp = Assert.IsType<BattleFighter>(flow.State.ActiveEnemyFighter).Hp;

        Assert.True(flow.Choose(BattleCommand.Attack, 0, new SequenceRng(0)));
        Assert.Equal(enemyHp, Assert.IsType<BattleFighter>(flow.State.ActiveEnemyFighter).Hp);
        Assert.NotEqual(BattleFlowPhase.PlayerCommand, flow.Phase);
    }

    /// <summary>Verifies that a damaging hit can wake a sleeping target and clear its control counter.</summary>
    [Fact]
    public void TryWakeOnHit_ClearsSleepWhenTheQualifiedRollSucceeds()
    {
        BattleState state = CreateState();
        BattleFighter enemy = Assert.IsType<BattleFighter>(state.ActiveEnemyFighter);
        enemy.Flags |= FighterFlags.Asleep;
        enemy.Sleep = 1;

        Assert.True(state.TryWakeOnHit(BattleSide.Enemy, damage: 100, new SequenceRng(0)));
        Assert.Equal(0, enemy.Sleep);
        Assert.Equal(0, enemy.Flags & FighterFlags.Asleep);
    }

    /// <summary>Verifies that a damaging player attack applies the wake rule to the sleeping enemy.</summary>
    [Fact]
    public void PlayerAttack_SleepingTarget_WakesThroughTheBattlePath()
    {
        BattleState state = CreateState();
        BattleFighter enemy = Assert.IsType<BattleFighter>(state.ActiveEnemyFighter);
        enemy.Flags |= FighterFlags.Asleep;
        enemy.Sleep = 1;

        Assert.True(state.PlayerAttack(new TechData { Accuracy = 128, Power = 20 }, new SequenceRng(0, 0, 0)));
        Assert.Equal(0, enemy.Flags & FighterFlags.Asleep);
    }

    /// <summary>Verifies that direct simulation calls reject actions which sleep greys in the command menu.</summary>
    [Fact]
    public void PlayerActions_SleepingFighter_RejectsDisabledCommands()
    {
        BattleState state = CreateState();
        Assert.IsType<BattleFighter>(state.ActivePlayerFighter).Flags |= FighterFlags.Asleep;

        Assert.False(state.PlayerAttack(new TechData(), new SequenceRng()));
        Assert.False(state.PlayerTechnique(new TechData(), new SequenceRng()));
        Assert.False(state.PlayerDigivolve(form: 2));
        Assert.False(state.PlayerFlee(new SequenceRng()));
    }

    /// <summary>Verifies that direct simulation calls reject actions which confusion greys in the command menu.</summary>
    [Fact]
    public void PlayerActions_ConfusedFighter_RejectsEveryCommandExceptAttack()
    {
        BattleState state = CreateState();
        Assert.IsType<BattleFighter>(state.ActivePlayerFighter).Flags |= FighterFlags.Confused;

        Assert.False(state.PlayerTechnique(new TechData(), new SequenceRng()));
        Assert.False(state.PlayerDigivolve(form: 2));
        Assert.False(state.PlayerSwitch(slot: 1));
        Assert.False(state.PlayerItem(new BattleItem(1, "heal", "active", 1)));
        Assert.False(state.PlayerFlee(new SequenceRng()));
    }

    private static BattleState CreateState()
    {
        var setup = new BattleSetup();
        setup.Partners[0] = Partner(id: 1);
        setup.Partners[1] = Partner(id: 2);
        setup.Enemies[0] = Enemy(id: 3);
        return BattleState.InitFighters(setup, digimon: 1, new SequenceRng(127));
    }

    private static PartnerSeed Partner(int id) => new()
    {
        Id = id,
        Hp = 100,
        MaxHp = 100,
        Mp = 100,
        MaxMp = 100,
        Stats = Stats(),
    };

    private static BattleEnemy Enemy(int id) => new()
    {
        Fighter = id,
        Hp = 100,
        Mp = 100,
        Strength = 16,
        BaseStats = Stats(),
    };

    private static BattleStats Stats()
    {
        var stats = new BattleStats();
        stats.Stats[BattleStat.Attack] = 20;
        stats.Stats[BattleStat.Defense] = 20;
        stats.Stats[BattleStat.Speed] = 100;
        return stats;
    }

    private sealed class SequenceRng(params int[] values) : IBattleRng
    {
        private readonly Queue<int> _values = new(values);

        public int NextValue() => _values.Count > 0 ? _values.Dequeue() : 0;
    }
}
