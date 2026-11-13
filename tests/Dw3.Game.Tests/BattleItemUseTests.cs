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

    /// <summary>Verifies that an enemy-targeted confusion item reaches the real player item flow and consumes stock when its source chance succeeds.</summary>
    [Fact]
    public void Choose_OffensiveConfusionItem_ConsumesOneOnSuccess()
    {
        BattleFlowController flow = CreateFlow(new BattleItem(0x56, "confuse", "enemy", 96));

        bool spent = flow.Choose(BattleCommand.Item, 0x56, new FixedRng());

        Assert.True(spent);
        Assert.Equal(BattleFlowPhase.EnemyTurn, flow.Phase);
        Assert.Equal(1, flow.ItemStock[0x56]);
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

    /// <summary>Verifies that a fixed elemental item changes the field, consumes one stock entry, and expires through the real scheduler consumer.</summary>
    [Fact]
    public void Choose_ElementBoost_ChangesFieldConsumesOneAndExpires()
    {
        BattleFlowController flow = CreateFlow(new BattleItem(0x4D, "boostElement", "field", 0));

        bool spent = flow.Choose(BattleCommand.Item, 0x4D, new FixedRng());

        Assert.True(spent);
        Assert.Equal(new ElementBoost(2, 64), flow.State.Boost);
        Assert.Equal(1, flow.ItemStock[0x4D]);
        AdvanceToElementExpiry(flow);
        Assert.Equal(ElementBoost.None, flow.State.Boost);
    }

    /// <summary>Verifies that the random elemental item selects the source range and magnitude through the shared battle random stream.</summary>
    [Fact]
    public void Choose_RandomElementBoost_UsesSharedRngAndConsumesOne()
    {
        BattleFlowController flow = CreateFlow(new BattleItem(0x54, "boostRandomElement", "field", 0));
        var rng = new CountingRng();

        bool spent = flow.Choose(BattleCommand.Item, 0x54, rng);

        Assert.True(spent);
        Assert.Equal(new ElementBoost(7, 127), flow.State.Boost);
        Assert.Equal(2, rng.Calls);
        Assert.Equal(1, flow.ItemStock[0x54]);
    }

    /// <summary>Verifies that an elemental field item with a non-field target is refused before stock, state, or random-stream mutation.</summary>
    [Fact]
    public void Choose_ElementBoostWithPartnerTarget_IsRefusedWithoutMutation()
    {
        BattleFlowController flow = CreateFlow(new BattleItem(0x4D, "boostElement", "partner", 0));
        var rng = new CountingRng();

        Assert.False(flow.Choose(BattleCommand.Item, 0x4D, rng));
        Assert.Equal(ElementBoost.None, flow.State.Boost);
        Assert.Equal(2, flow.ItemStock[0x4D]);
        Assert.Equal(0, rng.Calls);
    }

    /// <summary>Verifies that a zero-stock random elemental item is refused before it selects an element or schedules expiry.</summary>
    [Fact]
    public void Choose_RandomElementBoostWithZeroStock_IsRefusedWithoutMutation()
    {
        BattleFlowController flow = CreateFlow(new BattleItem(0x54, "boostRandomElement", "field", 0));
        flow.ItemStock[0x54] = 0;
        var rng = new CountingRng();

        Assert.False(flow.Choose(BattleCommand.Item, 0x54, rng));
        Assert.Equal(ElementBoost.None, flow.State.Boost);
        Assert.Equal(0, flow.ItemStock[0x54]);
        Assert.Equal(0, rng.Calls);
    }

    /// <summary>Verifies that a second field item replaces the first boost, consumes its own stock, and restores the original field after expiry.</summary>
    [Fact]
    public void Choose_ElementBoostRepeatedUse_ReplacesFieldAndRestoresOriginalAfterExpiry()
    {
        BattleFlowController flow = CreateFlow(new BattleItem(0x4D, "boostElement", "field", 0));
        flow.Items = new Dictionary<int, BattleItem>
        {
            [0x4D] = new(0x4D, "boostElement", "field", 0),
            [0x53] = new(0x53, "boostElement", "field", 0),
        };
        flow.ItemStock = new Dictionary<int, int> { [0x4D] = 1, [0x53] = 1 };

        Assert.True(flow.Choose(BattleCommand.Item, 0x4D, new FixedRng()));
        flow.EnemyAct(new FixedRng());
        Assert.True(flow.Choose(BattleCommand.Item, 0x53, new FixedRng()));

        Assert.Equal(new ElementBoost(8, 64), flow.State.Boost);
        Assert.Equal(0, flow.ItemStock[0x4D]);
        Assert.Equal(0, flow.ItemStock[0x53]);
        AdvanceToElementExpiry(flow);
        Assert.Equal(ElementBoost.None, flow.State.Boost);
    }

    /// <summary>Verifies that the drain item removes exactly 20 percent of the enemy maximum HP, restores the partner, and consumes stock through the player item flow.</summary>
    [Fact]
    public void Choose_DrainItem_TransfersTwentyPercentOfEnemyMaximumHp()
    {
        BattleFlowController flow = CreateFlow(new BattleItem(0x55, "drain", "enemy", 0));
        BattleFighter active = Assert.IsType<BattleFighter>(flow.State.ActivePlayerFighter);
        BattleFighter enemy = Assert.IsType<BattleFighter>(flow.State.ActiveEnemyFighter);
        active.Hp = 40;
        enemy.Hp = 90;
        enemy.MaxHp = 100;

        bool spent = flow.Choose(BattleCommand.Item, 0x55, new FixedRng());

        Assert.True(spent);
        Assert.Equal(60, active.Hp);
        Assert.Equal(70, enemy.Hp);
        Assert.Equal(1, flow.ItemStock[0x55]);
    }

    /// <summary>Verifies that confusion applies to the enemy through the item flow and consumes the source chance draw.</summary>
    [Fact]
    public void Choose_ConfuseItem_AppliesEnemyConfusionAndConsumesOne()
    {
        BattleFlowController flow = CreateFlow(new BattleItem(0x56, "confuse", "enemy", 96));
        BattleFighter enemy = Assert.IsType<BattleFighter>(flow.State.ActiveEnemyFighter);
        var rng = new OddCountingRng();

        bool spent = flow.Choose(BattleCommand.Item, 0x56, rng);

        Assert.True(spent);
        Assert.NotEqual(0, enemy.Flags & FighterFlags.Confused);
        Assert.Equal(1, flow.ItemStock[0x56]);
        Assert.True(rng.Calls > 0);
    }

    /// <summary>Verifies that the defense-weakening item applies its source attack raise and defense reduction to the enemy.</summary>
    [Fact]
    public void Choose_WeakenDefenseItem_ChangesEnemyAttackAndDefense()
    {
        BattleFlowController flow = CreateFlow(new BattleItem(0x57, "weakenDefense", "enemy", 0));
        BattleFighter enemy = Assert.IsType<BattleFighter>(flow.State.ActiveEnemyFighter);
        SetCombatStats(enemy, 100);

        bool spent = flow.Choose(BattleCommand.Item, 0x57, new FixedRng());

        Assert.True(spent);
        Assert.Equal(30, enemy.Boosts[0]);
        Assert.Equal(-50, enemy.Boosts[1]);
        Assert.Equal(1, flow.ItemStock[0x57]);
    }

    /// <summary>Verifies that the higher-stat item lowers the enemy attack to its defense and schedules a temporary enemy modifier.</summary>
    [Fact]
    public void Choose_LowerStatItem_LowersHigherEnemyStat()
    {
        BattleFlowController flow = CreateFlow(new BattleItem(0x58, "lowerStat", "enemy", 0));
        BattleFighter enemy = Assert.IsType<BattleFighter>(flow.State.ActiveEnemyFighter);
        enemy.Boosts[0] = 40;
        enemy.RefreshStats();

        bool spent = flow.Choose(BattleCommand.Item, 0x58, new FixedRng());

        Assert.True(spent);
        Assert.Equal(0, enemy.Boosts[0]);
        Assert.Equal(1, flow.ItemStock[0x58]);
    }

    /// <summary>Verifies that the speed item lowers the enemy by its source 128th amount and refuses zero stock before state or random mutation.</summary>
    [Fact]
    public void Choose_LowerSpeedItem_ChangesEnemyAndRejectsZeroStock()
    {
        BattleFlowController flow = CreateFlow(new BattleItem(0x59, "lowerSpeed", "enemy", 64));
        BattleFighter enemy = Assert.IsType<BattleFighter>(flow.State.ActiveEnemyFighter);
        enemy.Stats.Stats[BattleStat.Speed] = 100;
        enemy.RefreshStats();

        Assert.True(flow.Choose(BattleCommand.Item, 0x59, new FixedRng()));
        Assert.Equal(-50, enemy.Boosts[2]);
        flow.EnemyAct(new FixedRng());
        flow.ItemStock[0x59] = 0;
        var rng = new CountingRng();

        Assert.False(flow.Choose(BattleCommand.Item, 0x59, rng));
        Assert.Equal(-50, enemy.Boosts[2]);
        Assert.Equal(0, rng.Calls);
    }

    /// <summary>Verifies that a repeated confusion refusal preserves target state, stock, phase, and the shared random stream.</summary>
    [Fact]
    public void Choose_ConfuseItemAgainstConfusedEnemy_IsRefusedWithoutRandomMutation()
    {
        BattleFlowController flow = CreateFlow(new BattleItem(0x56, "confuse", "enemy", 96));
        BattleFighter enemy = Assert.IsType<BattleFighter>(flow.State.ActiveEnemyFighter);
        enemy.Flags |= FighterFlags.Confused;
        var rng = new CountingRng();

        bool spent = flow.Choose(BattleCommand.Item, 0x56, rng);

        Assert.False(spent);
        Assert.NotEqual(0, enemy.Flags & FighterFlags.Confused);
        Assert.Equal(BattleFlowPhase.PlayerCommand, flow.Phase);
        Assert.Equal(2, flow.ItemStock[0x56]);
        Assert.Equal(0, rng.Calls);
    }

    /// <summary>Verifies that the qualified item technique resolves from the started flow catalogue, damages the enemy, and consumes one item.</summary>
    [Fact]
    public void Choose_ItemTechnique_UsesQualifiedCatalogueTechniqueAndConsumesOne()
    {
        BattleFlowController flow = CreateFlow(new BattleItem(0x5A, "itemTech", "enemy", 0));
        BattleFighter enemy = Assert.IsType<BattleFighter>(flow.State.ActiveEnemyFighter);
        int hpBefore = enemy.Hp;

        bool spent = flow.Choose(BattleCommand.Item, 0x5A, new FixedRng());

        Assert.True(spent);
        Assert.True(enemy.Hp < hpBefore);
        Assert.Equal(1, flow.ItemStock[0x5A]);
    }

    /// <summary>Verifies that an item technique with no qualified catalogue entry is refused before state, stock, phase, or random mutation.</summary>
    [Fact]
    public void Choose_ItemTechniqueWithoutQualifiedCatalogueEntry_IsRefusedWithoutMutation()
    {
        BattleFlowController flow = CreateFlow(new BattleItem(0x5A, "itemTech", "enemy", 0), includeItemTechnique: false);
        BattleFighter enemy = Assert.IsType<BattleFighter>(flow.State.ActiveEnemyFighter);
        int hpBefore = enemy.Hp;
        var rng = new CountingRng();

        bool spent = flow.Choose(BattleCommand.Item, 0x5A, rng);

        Assert.False(spent);
        Assert.Equal(hpBefore, enemy.Hp);
        Assert.Equal(BattleFlowPhase.PlayerCommand, flow.Phase);
        Assert.Equal(2, flow.ItemStock[0x5A]);
        Assert.Equal(0, rng.Calls);
    }

    /// <summary>Verifies that item 0x5A rejects a non-enemy target before it can damage, consume stock, advance phase, or draw randomness.</summary>
    [Fact]
    public void Choose_ItemTechniqueWithInvalidTarget_IsRefusedWithoutMutation()
    {
        BattleFlowController flow = CreateFlow(new BattleItem(0x5A, "itemTech", "partner", 0));
        BattleFighter enemy = Assert.IsType<BattleFighter>(flow.State.ActiveEnemyFighter);
        int hpBefore = enemy.Hp;
        var rng = new CountingRng();

        bool spent = flow.Choose(BattleCommand.Item, 0x5A, rng);

        Assert.False(spent);
        Assert.Equal(hpBefore, enemy.Hp);
        Assert.Equal(BattleFlowPhase.PlayerCommand, flow.Phase);
        Assert.Equal(2, flow.ItemStock[0x5A]);
        Assert.Equal(0, rng.Calls);
    }

    /// <summary>Verifies that item 0x5A with zero stock is refused before it can damage, advance phase, or draw randomness.</summary>
    [Fact]
    public void Choose_ItemTechniqueWithZeroStock_IsRefusedWithoutMutation()
    {
        BattleFlowController flow = CreateFlow(new BattleItem(0x5A, "itemTech", "enemy", 0));
        BattleFighter enemy = Assert.IsType<BattleFighter>(flow.State.ActiveEnemyFighter);
        int hpBefore = enemy.Hp;
        flow.ItemStock[0x5A] = 0;
        var rng = new CountingRng();

        bool spent = flow.Choose(BattleCommand.Item, 0x5A, rng);

        Assert.False(spent);
        Assert.Equal(hpBefore, enemy.Hp);
        Assert.Equal(BattleFlowPhase.PlayerCommand, flow.Phase);
        Assert.Equal(0, flow.ItemStock[0x5A]);
        Assert.Equal(0, rng.Calls);
    }

    /// <summary>Advances ordinary turns until the field's scheduled expiry has been consumed.</summary>
    /// <param name="flow">Live battle flow owning the source scheduler consumer.</param>
    private static void AdvanceToElementExpiry(BattleFlowController flow)
    {
        for (int turn = 0; turn < 16 && flow.State.Boost != ElementBoost.None; turn++)
        {
            flow.EnemyAct(new FixedRng());
            _ = flow.Choose(BattleCommand.Attack, 0, new FixedRng());
        }
    }

    /// <summary>Creates a player-turn flow with one real item in its catalogue and bag.</summary>
    /// <param name="item">Item supplied to the flow catalogue.</param>
    /// <param name="includeItemTechnique">Whether the qualified source item technique is present in the started catalogue.</param>
    /// <returns>A started flow with two copies of the requested item.</returns>
    private static BattleFlowController CreateFlow(BattleItem item, bool includeItemTechnique = true)
    {
        var setup = new BattleSetup();
        setup.Partners[0] = Partner(1);
        if (item.Effect == "lowerSpeed")
        {
            setup.Partners[0]!.Stats.Stats[BattleStat.Speed] = 200;
        }

        int enemySpeed = item.Effect == "lowerSpeed" ? 100 : 0;
        setup.Enemies[0] = new BattleEnemy { Fighter = 2, Hp = 100, Mp = 100, Strength = 16, BaseStats = new BattleStats { Stats = [100, 100, 100, 100, enemySpeed] } };
        var flow = new BattleFlowController
        {
            Items = new Dictionary<int, BattleItem> { [item.Id] = item },
            ItemStock = new Dictionary<int, int> { [item.Id] = 2 },
            Techs = includeItemTechnique && item.Effect == "itemTech"
                ? new Dictionary<int, TechData> { [0x89] = new TechData { Power = 20, Icon = BattleConst.TechMagic } }
                : [],
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

    /// <summary>Counts a stable odd source draw for source half-chance item effects.</summary>
    private sealed class OddCountingRng : IBattleRng
    {
        /// <summary>Gets the number of observed source draws.</summary>
        public int Calls { get; private set; }

        /// <summary>Counts and returns an odd source value.</summary>
        /// <returns>The stable odd source value.</returns>
        public int NextValue()
        {
            Calls++;
            return 127;
        }
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
