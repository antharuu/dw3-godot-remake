using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests;

/// <summary>Verifies that a stocked battle item spends a turn only when its supported effect changes its valid target.</summary>
public sealed class BattleItemUseTests
{
    /// <summary>Verifies that a real healing item restores HP and decrements exactly one stock entry.</summary>
    [Fact]
    public void Choose_HealingItem_HealsActivePartnerAndConsumesOne()
    {
        BattleFlowController flow = CreateFlow(new BattleItem(0x2B, "heal", "partner", 500));
        BattleFighter active = Assert.IsType<BattleFighter>(flow.State.ActivePlayerFighter);
        active.Hp = 50;

        bool spent = flow.Choose(BattleCommand.Item, 0x2B, new FixedRng());

        Assert.True(spent);
        Assert.Equal(100, active.Hp);
        Assert.Equal(1, flow.ItemStock[0x2B]);
    }

    /// <summary>Verifies that the poison cure uses the established status semantics and consumes only after the cure changes the active partner.</summary>
    [Fact]
    public void Choose_PoisonCure_ClearsPoisonAndConsumesOne()
    {
        BattleFlowController flow = CreateFlow(new BattleItem(0x42, "cureStatus", "partner", 0));
        BattleFighter active = Assert.IsType<BattleFighter>(flow.State.ActivePlayerFighter);
        active.Flags |= FighterFlags.Poisoned;

        bool spent = flow.Choose(BattleCommand.Item, 0x42, new FixedRng());

        Assert.True(spent);
        Assert.Equal(0, active.Flags & FighterFlags.Poisoned);
        Assert.Equal(1, flow.ItemStock[0x42]);
    }

    /// <summary>Verifies that an unsupported enemy-targeted item neither spends the turn nor consumes stock.</summary>
    [Fact]
    public void Choose_UnsupportedOffensiveItem_IsRefusedWithoutConsumption()
    {
        BattleFlowController flow = CreateFlow(new BattleItem(0x56, "confuse", "enemy", 96));

        bool spent = flow.Choose(BattleCommand.Item, 0x56, new FixedRng());

        Assert.False(spent);
        Assert.Equal(BattleFlowPhase.PlayerCommand, flow.Phase);
        Assert.Equal(2, flow.ItemStock[0x56]);
    }

    /// <summary>Verifies that an otherwise supported item is refused without consumption when it cannot improve its valid target.</summary>
    [Fact]
    public void Choose_HealingItemAtFullHp_IsRefusedWithoutConsumption()
    {
        BattleFlowController flow = CreateFlow(new BattleItem(0x2B, "heal", "partner", 500));

        bool spent = flow.Choose(BattleCommand.Item, 0x2B, new FixedRng());

        Assert.False(spent);
        Assert.Equal(BattleFlowPhase.PlayerCommand, flow.Phase);
        Assert.Equal(2, flow.ItemStock[0x2B]);
    }

    /// <summary>Verifies that half-recovery is refused without a turn or stock loss when integer recovery rounds to zero.</summary>
    [Fact]
    public void Choose_HalfRecoveryWithZeroGain_IsRefusedWithoutConsumption()
    {
        BattleFlowController flow = CreateFlow(new BattleItem(74, "restoreHalfHpMp", "partner", 0));
        BattleFighter active = Assert.IsType<BattleFighter>(flow.State.ActivePlayerFighter);
        active.Mp = 0;
        active.MaxMp = 1;

        bool spent = flow.Choose(BattleCommand.Item, 74, new FixedRng());

        Assert.False(spent);
        Assert.Equal(BattleFlowPhase.PlayerCommand, flow.Phase);
        Assert.Equal(100, active.Hp);
        Assert.Equal(0, active.Mp);
        Assert.Equal(2, flow.ItemStock[74]);
    }

    /// <summary>Verifies that half recovery retains its positive capped HP and MP gains.</summary>
    [Fact]
    public void Choose_HalfRecoveryWithDeficits_RestoresCappedResourcesAndConsumesOne()
    {
        BattleFlowController flow = CreateFlow(new BattleItem(74, "restoreHalfHpMp", "party", 0));
        BattleFighter active = Assert.IsType<BattleFighter>(flow.State.ActivePlayerFighter);
        active.Hp = 60;
        active.Mp = 25;

        bool spent = flow.Choose(BattleCommand.Item, 74, new FixedRng());

        Assert.True(spent);
        Assert.Equal(100, active.Hp);
        Assert.Equal(75, active.Mp);
        Assert.Equal(1, flow.ItemStock[74]);
    }

    /// <summary>Verifies that half recovery rejects an unsupported target before it can spend the turn or stock.</summary>
    [Fact]
    public void Choose_HalfRecoveryWithEnemyTarget_IsRefusedWithoutConsumption()
    {
        BattleFlowController flow = CreateFlow(new BattleItem(74, "restoreHalfHpMp", "enemy", 0));
        BattleFighter active = Assert.IsType<BattleFighter>(flow.State.ActivePlayerFighter);
        active.Hp = 50;

        bool spent = flow.Choose(BattleCommand.Item, 74, new FixedRng());

        Assert.False(spent);
        Assert.Equal(BattleFlowPhase.PlayerCommand, flow.Phase);
        Assert.Equal(50, active.Hp);
        Assert.Equal(2, flow.ItemStock[74]);
    }

    /// <summary>Verifies that the speed item raises the active partner, spends one stock, and expires through the real turn scheduler.</summary>
    [Fact]
    public void Choose_SpeedBoost_ChangesActivePartnerAndExpiresThroughScheduler()
    {
        BattleFlowController flow = CreateFlow(new BattleItem(0x48, "boostSpeed", "partner", 128));
        BattleFighter active = Assert.IsType<BattleFighter>(flow.State.ActivePlayerFighter);
        active.Stats.Stats[BattleStat.Speed] = 50;
        active.RefreshStats();

        bool spent = flow.Choose(BattleCommand.Item, 0x48, new FixedRng());

        Assert.True(spent);
        Assert.Equal(50, active.Boosts[2]);
        Assert.Equal(1, flow.ItemStock[0x48]);
        AdvanceToBoostExpiry(flow);
        Assert.Equal(0, active.Boosts[2]);
    }

    /// <summary>Verifies that the attack item applies its source attack raise and defense trade-off through the real item flow.</summary>
    [Fact]
    public void Choose_AttackBoost_ChangesBothSourceStatsAndConsumesOne()
    {
        BattleFlowController flow = CreateFlow(new BattleItem(0x49, "boostAttack", "partner", 128));
        BattleFighter active = Assert.IsType<BattleFighter>(flow.State.ActivePlayerFighter);
        SetCombatStats(active, 50);

        bool spent = flow.Choose(BattleCommand.Item, 0x49, new FixedRng());

        Assert.True(spent);
        Assert.Equal(50, active.Boosts[0]);
        Assert.Equal(-12, active.Boosts[1]);
        Assert.Equal(1, flow.ItemStock[0x49]);
    }

    /// <summary>Verifies that the defense item applies its source defense raise and attack trade-off through the real item flow.</summary>
    [Fact]
    public void Choose_DefenseBoost_ChangesBothSourceStatsAndConsumesOne()
    {
        BattleFlowController flow = CreateFlow(new BattleItem(0x4A, "boostDefense", "partner", 128));
        BattleFighter active = Assert.IsType<BattleFighter>(flow.State.ActivePlayerFighter);
        SetCombatStats(active, 50);

        bool spent = flow.Choose(BattleCommand.Item, 0x4A, new FixedRng());

        Assert.True(spent);
        Assert.Equal(-12, active.Boosts[0]);
        Assert.Equal(50, active.Boosts[1]);
        Assert.Equal(1, flow.ItemStock[0x4A]);
    }

    /// <summary>Verifies that special can be applied once but is refused without stock loss while already active.</summary>
    [Fact]
    public void Choose_Special_RejectsRepeatedUseWithoutRngOrStockMutation()
    {
        BattleFlowController flow = CreateFlow(new BattleItem(0x4B, "special", "partner", 0));
        BattleFighter active = Assert.IsType<BattleFighter>(flow.State.ActivePlayerFighter);
        var rng = new CountingRng();

        Assert.True(flow.Choose(BattleCommand.Item, 0x4B, rng));
        flow.EnemyAct(rng);
        int callsBeforeRefusal = rng.Calls;

        Assert.False(flow.Choose(BattleCommand.Item, 0x4B, rng));
        Assert.True(active.Special);
        Assert.Equal(callsBeforeRefusal, rng.Calls);
        Assert.Equal(1, flow.ItemStock[0x4B]);
    }

    /// <summary>Verifies that the gauge item caps at the source maximum and refuses a full gauge without consumption.</summary>
    [Fact]
    public void Choose_FillGauge_CapsAt999AndRefusesFullGauge()
    {
        BattleFlowController flow = CreateFlow(new BattleItem(0x4C, "fillGauge", "partner", 50));
        BattleFighter active = Assert.IsType<BattleFighter>(flow.State.ActivePlayerFighter);
        active.BlastGauge = 980;

        Assert.True(flow.Choose(BattleCommand.Item, 0x4C, new FixedRng()));
        Assert.Equal(999, active.BlastGauge);
        Assert.Equal(1, flow.ItemStock[0x4C]);
        flow.EnemyAct(new FixedRng());

        Assert.False(flow.Choose(BattleCommand.Item, 0x4C, new FixedRng()));
        Assert.Equal(999, active.BlastGauge);
        Assert.Equal(1, flow.ItemStock[0x4C]);
    }

    /// <summary>Verifies that a partner boost rejects a wrong target before stat, stock, or random-stream mutation.</summary>
    [Fact]
    public void Choose_SpeedBoostWithEnemyTarget_IsRefusedWithoutMutation()
    {
        BattleFlowController flow = CreateFlow(new BattleItem(0x48, "boostSpeed", "enemy", 128));
        BattleFighter active = Assert.IsType<BattleFighter>(flow.State.ActivePlayerFighter);
        var rng = new CountingRng();

        Assert.False(flow.Choose(BattleCommand.Item, 0x48, rng));
        Assert.Equal(0, active.Boosts[2]);
        Assert.Equal(2, flow.ItemStock[0x48]);
        Assert.Equal(0, rng.Calls);
    }

    /// <summary>Verifies that a known partner boost with zero stock is refused before state or random-stream mutation.</summary>
    [Fact]
    public void Choose_SpeedBoostWithZeroStock_IsRefusedWithoutMutation()
    {
        BattleFlowController flow = CreateFlow(new BattleItem(0x48, "boostSpeed", "partner", 128));
        BattleFighter active = Assert.IsType<BattleFighter>(flow.State.ActivePlayerFighter);
        flow.ItemStock[0x48] = 0;
        var rng = new CountingRng();

        Assert.False(flow.Choose(BattleCommand.Item, 0x48, rng));
        Assert.Equal(0, active.Boosts[2]);
        Assert.Equal(0, flow.ItemStock[0x48]);
        Assert.Equal(0, rng.Calls);
    }

    /// <summary>Creates a player-turn flow with one real item in its catalogue and bag.</summary>
    /// <param name="item">Item supplied to the flow catalogue.</param>
    /// <returns>A started flow with two copies of the requested item.</returns>
    private static BattleFlowController CreateFlow(BattleItem item)
    {
        var setup = new BattleSetup();
        setup.Partners[0] = Partner(1);
        setup.Enemies[0] = new BattleEnemy { Fighter = 2, Hp = 100, Mp = 100, Strength = 16, BaseStats = new BattleStats() };
        var flow = new BattleFlowController
        {
            Items = new Dictionary<int, BattleItem> { [item.Id] = item },
            ItemStock = new Dictionary<int, int> { [item.Id] = 2 },
        };
        flow.Start(BattleState.InitFighters(setup, 1, new FixedRng()));
        return flow;
    }

    /// <summary>Creates a deterministic full-health player partner.</summary>
    /// <param name="id">Partner fighter id.</param>
    /// <returns>A partner seed accepted by the battle-state factory.</returns>
    private static PartnerSeed Partner(int id) => new()
    {
        Id = id,
        Hp = 100,
        MaxHp = 100,
        Mp = 100,
        MaxMp = 100,
        Stats = new BattleStats(),
    };

    /// <summary>Sets deterministic non-zero attack and defense bases for source trade-off assertions.</summary>
    /// <param name="fighter">Active partner whose base combat stats are initialized.</param>
    /// <param name="value">Shared fabricated base stat.</param>
    private static void SetCombatStats(BattleFighter fighter, int value)
    {
        fighter.Stats.Stats[BattleStat.Attack] = value;
        fighter.Stats.Stats[BattleStat.Defense] = value;
        fighter.RefreshStats();
    }

    /// <summary>Advances ordinary turns until the queued speed expiry has been consumed.</summary>
    /// <param name="flow">Live battle flow owning the source scheduler consumer.</param>
    private static void AdvanceToBoostExpiry(BattleFlowController flow)
    {
        for (int turn = 0; turn < 8 && flow.State.ActivePlayerFighter!.Boosts[2] != 0; turn++)
        {
            flow.EnemyAct(new FixedRng());
            _ = flow.Choose(BattleCommand.Attack, 0, new FixedRng());
        }
    }

    /// <summary>Supplies a stable opening-order draw without recording unrelated item RNG.</summary>
    private sealed class FixedRng : IBattleRng
    {
        /// <summary>Returns the same neutral source draw for each caller.</summary>
        /// <returns>The neutral source value.</returns>
        public int NextValue() => 127;
    }

    /// <summary>Counts source random draws while providing a stable neutral value.</summary>
    private sealed class CountingRng : IBattleRng
    {
        /// <summary>Gets the number of draws observed through the real flow.</summary>
        public int Calls { get; private set; }

        /// <summary>Counts and returns one neutral source draw.</summary>
        /// <returns>The neutral source value.</returns>
        public int NextValue()
        {
            Calls++;
            return 2000;
        }
    }
}
