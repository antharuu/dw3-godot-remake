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

    /// <summary>Supplies a stable opening-order draw without recording unrelated item RNG.</summary>
    private sealed class FixedRng : IBattleRng
    {
        /// <summary>Returns the same neutral source draw for each caller.</summary>
        /// <returns>The neutral source value.</returns>
        public int NextValue() => 127;
    }
}
