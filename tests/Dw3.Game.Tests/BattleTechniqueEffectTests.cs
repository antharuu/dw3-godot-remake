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

    /// <summary>Verifies that encounter blocks reject their matching player-originated control effect through the real technique path.</summary>
    [Theory]
    [InlineData(BattleTechniqueEffect.Paralysis, 1, FighterFlags.Paralyzed)]
    [InlineData(BattleTechniqueEffect.Confusion, 2, FighterFlags.Confused)]
    [InlineData(BattleTechniqueEffect.Sleep, 3, FighterFlags.Asleep)]
    public void PlayerTechnique_BlockedControlEffect_DoesNotApplyTheBlockedStatus(int effect, int block, int flag)
    {
        BattleState state = CreateState();
        state.Blocks[block] = 1;
        var technique = new TechData
        {
            Accuracy = 128,
            Effect = effect,
            EffectChance = 127,
            EffectPower = 20,
            Power = 1,
        };

        Assert.True(state.PlayerTechnique(technique, new SequenceRng(0, 0, 0)));

        Assert.Equal(0, Assert.IsType<BattleFighter>(state.ActiveEnemyFighter).Flags & flag);
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

    /// <summary>Verifies that a landed drain technique restores the attacker by its extracted share of actual damage without exceeding maximum HP.</summary>
    [Fact]
    public void PlayerTechnique_LandedDrain_RestoresAttackerByEffectPowerAndCapsAtMaximum()
    {
        BattleState state = CreateState();
        var player = Assert.IsType<BattleFighter>(state.ActivePlayerFighter);
        player.Hp = 90;
        var technique = new TechData { Accuracy = 128, Effect = BattleTechniqueEffect.Drain, EffectChance = 127, EffectPower = 128, Power = 20 };

        Assert.True(state.PlayerTechnique(technique, new SequenceRng(0, 0)));

        Assert.Equal(player.MaxHp, player.Hp);
    }

    /// <summary>Verifies that a player drain blocked by the encounter neither heals nor consumes an effect RNG draw.</summary>
    [Fact]
    public void PlayerTechnique_BlockedDrain_DoesNotHealOrConsumeEffectRandom()
    {
        BattleState state = CreateState();
        state.Blocks[BattleConst.BlockDrain] = 1;
        var player = Assert.IsType<BattleFighter>(state.ActivePlayerFighter);
        player.Hp = 50;
        var rng = new CountingRng(0, 0);
        var technique = new TechData { Accuracy = 128, Effect = BattleTechniqueEffect.Drain, EffectChance = 127, EffectPower = 128, Power = 20 };

        Assert.True(state.PlayerTechnique(technique, rng));

        Assert.Equal(50, player.Hp);
        Assert.Equal(1, rng.CallCount);
    }

    /// <summary>
    /// Verifies that a drain technique that KOs the target still heals the attacker from the
    /// formula-resolved damage, not from the HP actually removed (which is clamped to the target's
    /// remaining HP). With attacker HP=50, resolved damage=20 and EffectPower=128 the heal must be 20
    /// (attacker reaches 70), never 5 (clamped damage) or 0 (guard rejected on KO).
    /// </summary>
    [Fact]
    public void PlayerTechnique_LethalDrainHit_RestoresAttackerUsingResolvedDamage()
    {
        BattleState state = CreateState();
        var player = Assert.IsType<BattleFighter>(state.ActivePlayerFighter);
        var enemy = Assert.IsType<BattleFighter>(state.ActiveEnemyFighter);
        player.Hp = 50;
        enemy.Hp = 5;
        var technique = new TechData { Accuracy = 128, Effect = BattleTechniqueEffect.Drain, EffectChance = 127, EffectPower = 128, Power = 20 };

        Assert.True(state.PlayerTechnique(technique, new SequenceRng(0, 0)));

        Assert.Equal(0, enemy.Hp);
        var singleEvent = Assert.Single(state.LastHitEvents); // exactly one hit event
        Assert.Equal(5, singleEvent.Damage); // BattleHitEvent.Damage = HP actually removed (unchanged)
        Assert.Equal(70, player.Hp); // 50 + resolved(20)*128/128 = 70, NOT 50 (guard) or 55 (clamped*factor)
    }

    /// <summary>Verifies that a successful player steal transfers the enemy-held item and clears the source holder.</summary>
    [Fact]
    public void PlayerTechnique_SuccessfulSteal_TransfersTheHeldItem()
    {
        BattleState state = CreateState();
        var enemy = Assert.IsType<BattleFighter>(state.ActiveEnemyFighter);
        enemy.Item = 77;
        enemy.ItemChance = 1024;
        var technique = new TechData { Accuracy = 128, Effect = BattleTechniqueEffect.Steal, EffectChance = 64, Power = 20 };

        Assert.True(state.PlayerTechnique(technique, new SequenceRng(0, 0)));

        Assert.Equal(77, state.PendingStolenItem);
        Assert.Equal(0, enemy.Item);
    }

    /// <summary>Verifies that no item holder makes a steal refusal without an additional effect random draw or state change.</summary>
    [Fact]
    public void PlayerTechnique_StealWithoutHeldItem_DoesNotConsumeEffectRandom()
    {
        BattleState state = CreateState();
        var rng = new CountingRng(0, 0);
        var technique = new TechData { Accuracy = 128, Effect = BattleTechniqueEffect.Steal, EffectChance = 64, Power = 20 };

        Assert.True(state.PlayerTechnique(technique, rng));

        Assert.Null(state.PendingStolenItem);
        Assert.Equal(1, rng.CallCount);
    }

    /// <summary>Verifies that the enemy MP drain takes its extracted maximum-MP share, transfers only the available amount and caps the recipient.</summary>
    [Fact]
    public void EnemyTechnique_DrainMp_TransfersAvailableShareAndCapsRecipient()
    {
        BattleState state = CreateState();
        var player = Assert.IsType<BattleFighter>(state.ActivePlayerFighter);
        var enemy = Assert.IsType<BattleFighter>(state.ActiveEnemyFighter);
        player.Mp = 30;
        enemy.Mp = 90;
        var technique = new TechData { Effect = BattleTechniqueEffect.DrainMp, EffectPower = 64 };

        Assert.True(state.EnemyTechnique(technique, new SequenceRng()));

        Assert.Equal(0, player.Mp);
        Assert.Equal(enemy.MaxMp, enemy.Mp);
        Assert.Empty(state.LastHitEvents);
    }

    /// <summary>Verifies that a player cannot execute the enemy-targeted MP drain and therefore spends neither MP nor turn RNG.</summary>
    [Fact]
    public void PlayerTechnique_EnemyTargetedDrainMp_IsRefusedBeforeResources()
    {
        BattleState state = CreateState();
        var player = Assert.IsType<BattleFighter>(state.ActivePlayerFighter);
        var rng = new CountingRng(0);
        var technique = new TechData { Mp = 20, Effect = BattleTechniqueEffect.DrainMp, EffectPower = 64 };

        Assert.False(state.PlayerTechnique(technique, rng));

        Assert.Equal(100, player.Mp);
        Assert.Equal(0, rng.CallCount);
    }

    /// <summary>Verifies that an effect resolver refuses an absent target without changing fighters or consuming random input.</summary>
    [Fact]
    public void TechniqueEffect_MissingTarget_IsRefusedWithoutRandomInput()
    {
        var state = new BattleState
        {
            Players = [new BattleFighter { Hp = 100, MaxHp = 100 }],
            ActivePlayer = 0,
            ActiveEnemy = -1,
        };
        var rng = new CountingRng(0);

        Assert.True(BattleTechniqueEffectResolver.ApplyLanded(state, BattleSide.Player, new TechData { Effect = BattleTechniqueEffect.Drain, EffectChance = 127, EffectPower = 128 }, rng, 10));

        Assert.Equal(100, Assert.IsType<BattleFighter>(state.ActivePlayerFighter).Hp);
        Assert.Equal(0, rng.CallCount);
    }

    /// <summary>Verifies that a successful steal via the flow controller adds the item to ItemStock and clears the pending state.</summary>
    [Fact]
    public void ChooseTechnique_SuccessfulSteal_AddsItemToFlowControllerStockAndClearsPending()
    {
        BattleState state = CreateState();
        var enemy = Assert.IsType<BattleFighter>(state.ActiveEnemyFighter);
        enemy.Item = 77;
        enemy.ItemChance = 1024;
        const int techniqueId = 1;
        var stealTech = new TechData { Accuracy = 128, Effect = BattleTechniqueEffect.Steal, EffectChance = 64, Power = 20 };
        var flow = new BattleFlowController
        {
            PlayerTechniques = [new BattleTechnique(techniqueId, "Steal", stealTech)],
        };
        flow.Start(state);

        Assert.True(flow.Choose(BattleCommand.Technique, techniqueId, new SequenceRng(0, 0)));

        Assert.Equal(1, flow.ItemStock.GetValueOrDefault(77));
        Assert.Null(state.PendingStolenItem);
    }

    /// <summary>Verifies that a second steal on an item-less enemy adds nothing further to ItemStock.</summary>
    [Fact]
    public void ChooseTechnique_RepeatedStealOnEmptyEnemy_DoesNotIncrementStock()
    {
        BattleState state = CreateState();
        var enemy = Assert.IsType<BattleFighter>(state.ActiveEnemyFighter);
        enemy.Item = 77;
        enemy.ItemChance = 1024;
        const int techniqueId = 1;
        var stealTech = new TechData { Accuracy = 128, Effect = BattleTechniqueEffect.Steal, EffectChance = 64, Power = 20 };
        var flow = new BattleFlowController
        {
            PlayerTechniques = [new BattleTechnique(techniqueId, "Steal", stealTech)],
        };
        flow.Start(state);
        Assert.True(flow.Choose(BattleCommand.Technique, techniqueId, new SequenceRng(0, 0)));
        flow.EnemyAct(new SequenceRng());
        Assert.True(flow.Choose(BattleCommand.Technique, techniqueId, new SequenceRng(0, 0)));

        Assert.Equal(1, flow.ItemStock.GetValueOrDefault(77));
    }

    /// <summary>
    /// Verifies that stealing when the player already holds 99 of an item caps the stock at 99,
    /// removes the item from the enemy and clears the pending state — the source contract retires
    /// the enemy item regardless of the receiver stock, and the receiver must not overflow past 99.
    /// </summary>
    [Fact]
    public void ChooseTechnique_StealAtMaxStock_CapsStockAt99AndRemovesEnemyItem()
    {
        BattleState state = CreateState();
        var enemy = Assert.IsType<BattleFighter>(state.ActiveEnemyFighter);
        enemy.Item = 77;
        enemy.ItemChance = 1024;
        const int techniqueId = 1;
        var stealTech = new TechData { Accuracy = 128, Effect = BattleTechniqueEffect.Steal, EffectChance = 64, Power = 20 };
        var flow = new BattleFlowController
        {
            PlayerTechniques = [new BattleTechnique(techniqueId, "Steal", stealTech)],
            ItemStock = new Dictionary<int, int> { [77] = 99 },
        };
        flow.Start(state);

        Assert.True(flow.Choose(BattleCommand.Technique, techniqueId, new SequenceRng(0, 0)));

        Assert.Equal(99, flow.ItemStock.GetValueOrDefault(77)); // must NOT go to 100
        Assert.Equal(0, enemy.Item); // item still removed from enemy
        Assert.Null(state.PendingStolenItem); // consumed by flow
    }

    /// <summary>Verifies that TakePendingStolenItem returns the item on the first call and null on the second, proving exactly-once consumption.</summary>
    [Fact]
    public void TakePendingStolenItem_ReturnsItemOnFirstCallAndNullOnSecond()
    {
        BattleState state = CreateState();
        var enemy = Assert.IsType<BattleFighter>(state.ActiveEnemyFighter);
        enemy.Item = 77;
        enemy.ItemChance = 1024;
        var technique = new TechData { Accuracy = 128, Effect = BattleTechniqueEffect.Steal, EffectChance = 64, Power = 20 };

        Assert.True(state.PlayerTechnique(technique, new SequenceRng(0, 0)));

        Assert.Equal(77, state.TakePendingStolenItem());
        Assert.Null(state.TakePendingStolenItem());
    }

    /// <summary>Verifies that a landed enemy NoSwitch technique locks only the active player and spends its exact chance roll.</summary>
    [Fact]
    public void EnemyTechnique_LandedNoSwitch_LocksOnlyActivePlayer()
    {
        BattleState state = CreateState();
        var player = Assert.IsType<BattleFighter>(state.ActivePlayerFighter);
        var rng = new CountingRng(0, 0, 0);
        var technique = new TechData { Accuracy = 128, Effect = BattleTechniqueEffect.NoSwitch, EffectChance = 127, EffectPower = 100, Power = 1 };

        Assert.True(state.EnemyTechnique(technique, rng));

        Assert.NotEqual(0, player.Flags & FighterFlags.NoSwitch);
        Assert.Equal(3, rng.CallCount);
    }

    /// <summary>Verifies that a failed NoDigivolve chance consumes no duration random draw and leaves the active player unrestricted.</summary>
    [Fact]
    public void EnemyTechnique_FailedNoDigivolve_DoesNotScheduleOrLock()
    {
        BattleState state = CreateState();
        var player = Assert.IsType<BattleFighter>(state.ActivePlayerFighter);
        var rng = new CountingRng(0, 127);
        var technique = new TechData { Accuracy = 128, Effect = BattleTechniqueEffect.NoDigivolve, EffectChance = 127, EffectPower = 1, Power = 1 };

        Assert.True(state.EnemyTechnique(technique, rng));

        Assert.Equal(0, player.Flags & FighterFlags.NoDigivolve);
        Assert.Equal(2, rng.CallCount);
    }

    /// <summary>Verifies that a landed player attack-lowering technique changes only the active enemy by its extracted power.</summary>
    [Fact]
    public void PlayerTechnique_LandedLowerAttack_LowersOnlyTheActiveEnemy()
    {
        BattleState state = CreateState();
        var enemy = Assert.IsType<BattleFighter>(state.ActiveEnemyFighter);
        var technique = new TechData { Accuracy = 128, Effect = BattleTechniqueEffect.LowerAttack, EffectPower = 64, Power = 1 };

        Assert.True(state.PlayerTechnique(technique, new CountingRng(0, 0, 0)));

        Assert.Equal(-10, enemy.Boosts[0]);
        Assert.Equal(10, enemy.Stats.Stats[BattleStat.Attack]);
    }

    /// <summary>Verifies that a blocked player attack-lowering technique lands without changing the enemy or consuming an expiry roll.</summary>
    [Fact]
    public void PlayerTechnique_BlockedLowerAttack_DoesNotModifyTheEnemy()
    {
        BattleState state = CreateState();
        state.Blocks[BattleConst.BlockLowerAttack] = 1;
        var enemy = Assert.IsType<BattleFighter>(state.ActiveEnemyFighter);
        var rng = new CountingRng(0, 0);
        var technique = new TechData { Accuracy = 128, Effect = BattleTechniqueEffect.LowerAttack, EffectPower = 64, Power = 1 };

        Assert.True(state.PlayerTechnique(technique, rng));

        Assert.Equal(0, enemy.Boosts[0]);
        Assert.Equal(20, enemy.Stats.Stats[BattleStat.Attack]);
        Assert.Equal(1, rng.CallCount);
    }

    /// <summary>Verifies that restriction expiry clears only its own flag before the next command is exposed.</summary>
    [Fact]
    public void EnemyTechnique_NoSwitch_ExpiresWithoutClearingNoDigivolve()
    {
        BattleState state = CreateState();
        var player = Assert.IsType<BattleFighter>(state.ActivePlayerFighter);
        player.Flags |= FighterFlags.NoDigivolve;
        var technique = new TechData { Accuracy = 128, Effect = BattleTechniqueEffect.NoSwitch, EffectChance = 127, EffectPower = 1, Power = 1 };

        Assert.True(state.EnemyTechnique(technique, new CountingRng(0, 0, 0)));
        state.EnemyAttack(new TechData { Accuracy = 128, Power = 0 }, new CountingRng());

        Assert.Equal(0, player.Flags & FighterFlags.NoSwitch);
        Assert.NotEqual(0, player.Flags & FighterFlags.NoDigivolve);
    }

    /// <summary>Verifies that one successful status raise uses its selected persistent partner status without changing a reserve.</summary>
    [Fact]
    public void PlayerTechnique_RaiseOneStatus_ChangesSelectedActivePartnerStatusOnly()
    {
        BattleState state = CreateState();
        var technique = new TechData { Accuracy = 128, Effect = BattleTechniqueEffect.RaiseOneStatus, EffectChance = 127, EffectPower = 7, Power = 1 };

        Assert.True(state.PlayerTechnique(technique, new CountingRng(0, 1, 0)));

        Assert.Equal([0, 7, 0], state.Players[0]!.PersistentStatuses);
        Assert.Equal([0, 0, 0], state.Players[1]!.PersistentStatuses);
    }

    /// <summary>Verifies that each-status raises roll independently and leave failed statuses unchanged.</summary>
    [Fact]
    public void PlayerTechnique_RaiseEachStatus_RollsEveryStatusIndependently()
    {
        BattleState state = CreateState();
        var technique = new TechData { Accuracy = 128, Effect = BattleTechniqueEffect.RaiseEachStatus, EffectChance = 64, EffectPower = 5, Power = 1 };

        Assert.True(state.PlayerTechnique(technique, new CountingRng(0, 0, 0, 64, 63)));

        Assert.Equal([5, 5, 0], state.Players[0]!.PersistentStatuses);
    }

    /// <summary>Verifies that an all-status raise has one chance roll and changes every active partner status together.</summary>
    [Fact]
    public void PlayerTechnique_RaiseAllStatus_SuccessRaisesEveryActivePartnerStatus()
    {
        BattleState state = CreateState();
        var technique = new TechData { Accuracy = 128, Effect = BattleTechniqueEffect.RaiseAllStatus, EffectChance = 127, EffectPower = 3, Power = 1 };

        Assert.True(state.PlayerTechnique(technique, new CountingRng(0, 0, 0)));

        Assert.Equal([3, 3, 3], state.Players[0]!.PersistentStatuses);
    }

    /// <summary>Verifies that every landed status-raise family persists on a lethal hit without mutating a reserve partner or enemy.</summary>
    /// <param name="effect">Status-raise family exercised after the hit.</param>
    /// <param name="randomValues">Accuracy, selection when needed, and source chance rolls.</param>
    /// <param name="expectedStatuses">Persistent status values expected on the active partner.</param>
    [Theory]
    [InlineData(BattleTechniqueEffect.RaiseOneStatus, new[] { 0, 1, 0 }, new[] { 0, 3, 0 })]
    [InlineData(BattleTechniqueEffect.RaiseEachStatus, new[] { 0, 0, 0, 0 }, new[] { 3, 3, 3 })]
    [InlineData(BattleTechniqueEffect.RaiseAllStatus, new[] { 0, 0 }, new[] { 3, 3, 3 })]
    public void PlayerTechnique_LethalStatusRaise_PersistsOnlyOnTheActivePartner(int effect, int[] randomValues, int[] expectedStatuses)
    {
        BattleState state = CreateState(enemyHp: 5, includeEnemyReserve: true);
        var technique = new TechData { Accuracy = 128, Effect = effect, EffectChance = 127, EffectPower = 3, Power = 20 };

        Assert.True(state.PlayerTechnique(technique, new SequenceRng(randomValues)));

        Assert.Equal(5, Assert.Single(state.LastHitEvents).Damage);
        Assert.Equal(0, state.Enemies[0]!.Hp);
        Assert.Equal(BattleOutcome.None, state.Outcome);
        Assert.Equal(expectedStatuses, state.Players[0]!.PersistentStatuses);
        Assert.Equal([0, 0, 0], state.Players[1]!.PersistentStatuses);
        Assert.Equal(100, state.Enemies[1]!.Hp);
    }

    /// <summary>Verifies that an all-status raise remains persistent when its one landed hit wins the battle.</summary>
    [Fact]
    public void PlayerTechnique_LethalRaiseAllStatus_WinsAndPreservesTheHitDamage()
    {
        BattleState state = CreateState(enemyHp: 5, includeEnemyReserve: false);
        var technique = new TechData { Accuracy = 128, Effect = BattleTechniqueEffect.RaiseAllStatus, EffectChance = 127, EffectPower = 3, Power = 20 };

        Assert.True(state.PlayerTechnique(technique, new SequenceRng(0, 0)));

        Assert.Equal(5, Assert.Single(state.LastHitEvents).Damage);
        Assert.Equal(BattleOutcome.Won, state.Outcome);
        Assert.Equal([3, 3, 3], state.Players[0]!.PersistentStatuses);
        Assert.Equal([0, 0, 0], state.Players[1]!.PersistentStatuses);
    }

    /// <summary>Verifies that a status raise remains refused before an action when no active enemy exists.</summary>
    [Fact]
    public void PlayerTechnique_StatusRaiseWithoutActiveEnemy_DoesNotSpendOrPersist()
    {
        BattleState state = CreateState();
        state.Enemies[0] = null;
        var technique = new TechData { Accuracy = 128, Effect = BattleTechniqueEffect.RaiseAllStatus, EffectChance = 127, EffectPower = 3, Power = 20 };

        Assert.False(state.PlayerTechnique(technique, new SequenceRng(0, 0)));

        Assert.Equal([0, 0, 0], state.Players[0]!.PersistentStatuses);
    }

    /// <summary>Verifies that an accepted enemy effect-7 action returns the transformed active partner to its base form without changing HP or MP.</summary>
    [Fact]
    public void EnemyTechnique_AcceptedDedigivolve_ReturnsOnlyTheTransformedActivePartnerToBaseForm()
    {
        BattleState state = CreateState();
        BattleFighter player = Assert.IsType<BattleFighter>(state.ActivePlayerFighter);
        player.Id = 99;
        player.Hp = 71;
        player.Mp = 43;
        player.Charge = 32;
        var technique = new TechData { Icon = 2, Accuracy = 128, Effect = BattleTechniqueEffect.Dedigivolve, EffectChance = 127, Power = 0 };

        Assert.True(state.EnemyTechnique(technique, new SequenceRng(0, 0)));

        Assert.Equal(1, player.Id);
        Assert.Equal(71, player.Hp);
        Assert.Equal(43, player.Mp);
        Assert.Equal(0, player.Charge);
    }

    /// <summary>Verifies that a rejected effect-7 chance preserves the transformed partner and consumes only the landed-hit and chance draws.</summary>
    [Fact]
    public void EnemyTechnique_RejectedDedigivolveRoll_PreservesTheTransformedPartner()
    {
        BattleState state = CreateState();
        BattleFighter player = Assert.IsType<BattleFighter>(state.ActivePlayerFighter);
        player.Id = 99;
        player.Temporary = true;
        player.Hp = 71;
        player.Mp = 43;
        player.Charge = 32;
        var rng = new CountingRng(0, 1);
        var technique = new TechData { Accuracy = 128, Effect = BattleTechniqueEffect.Dedigivolve, EffectChance = 1, Power = 0 };

        Assert.True(state.EnemyTechnique(technique, rng));

        Assert.Equal(99, player.Id);
        Assert.True(player.Temporary);
        Assert.Equal(71, player.Hp);
        Assert.Equal(43, player.Mp);
        Assert.Equal(32, player.Charge);
        Assert.Equal(2, rng.CallCount);
    }

    /// <summary>Verifies that a partner in its base form refuses effect 7 before its chance roll and preserves temporary battle resources.</summary>
    [Fact]
    public void EnemyTechnique_BaseFormDedigivolve_DoesNotConsumeEffectChanceOrMutate()
    {
        BattleState state = CreateState();
        BattleFighter player = Assert.IsType<BattleFighter>(state.ActivePlayerFighter);
        player.Charge = 32;
        var rng = new CountingRng(0, 0);
        var technique = new TechData { Accuracy = 128, Effect = BattleTechniqueEffect.Dedigivolve, EffectChance = 127, Power = 0 };

        Assert.True(state.EnemyTechnique(technique, rng));

        Assert.Equal(1, player.Id);
        Assert.Equal(32, player.Charge);
        Assert.Equal(1, rng.CallCount);
    }

    /// <summary>Verifies that every registered base identity refuses enemy effect 7 before its chance roll, even when that base belongs to an absent partner.</summary>
    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(0x17F)]
    public void EnemyTechnique_RegisteredBaseFormDedigivolve_DoesNotConsumeEffectChanceOrMutate(int baseId)
    {
        BattleState state = CreateState();
        state.StarterDigimonIds = [1, 3, 0x17F];
        BattleFighter player = Assert.IsType<BattleFighter>(state.ActivePlayerFighter);
        player.Id = baseId;
        player.Hp = 71;
        player.Mp = 43;
        player.Charge = 32;
        var rng = new CountingRng(0, 0);
        var technique = new TechData { Icon = 2, Accuracy = 128, Effect = BattleTechniqueEffect.Dedigivolve, EffectChance = 127, Power = 0 };

        Assert.True(state.EnemyTechnique(technique, rng));

        Assert.Equal(baseId, player.Id);
        Assert.Equal(71, player.Hp);
        Assert.Equal(43, player.Mp);
        Assert.Equal(32, player.Charge);
        Assert.Equal(1, rng.CallCount);
    }

    /// <summary>Verifies that an unregistered non-base form remains eligible for effect 7.</summary>
    [Fact]
    public void EnemyTechnique_UnregisteredFormDedigivolve_ConsumesEffectChanceAndReturnsToOwnerBase()
    {
        BattleState state = CreateState();
        state.StarterDigimonIds = [1, 3, 0x17F];
        BattleFighter player = Assert.IsType<BattleFighter>(state.ActivePlayerFighter);
        player.Id = 99;
        player.Charge = 32;
        var rng = new CountingRng(0, 0);
        var technique = new TechData { Icon = 2, Accuracy = 128, Effect = BattleTechniqueEffect.Dedigivolve, EffectChance = 127, Power = 0 };

        Assert.True(state.EnemyTechnique(technique, rng));

        Assert.Equal(1, player.Id);
        Assert.Equal(0, player.Charge);
        Assert.Equal(2, rng.CallCount);
    }

    /// <summary>Verifies that a temporary transformed form follows the same source-form boundary and loses only its temporary form state.</summary>
    [Fact]
    public void EnemyTechnique_TemporaryDedigivolve_ClearsTemporaryFormWithoutChangingHpOrMp()
    {
        BattleState state = CreateState();
        BattleFighter player = Assert.IsType<BattleFighter>(state.ActivePlayerFighter);
        player.Id = 99;
        player.Temporary = true;
        player.Hp = 71;
        player.Mp = 43;
        var technique = new TechData { Accuracy = 128, Effect = BattleTechniqueEffect.Dedigivolve, EffectChance = 127, Power = 0 };

        Assert.True(state.EnemyTechnique(technique, new SequenceRng(0, 0)));

        Assert.Equal(1, player.Id);
        Assert.False(player.Temporary);
        Assert.Equal(71, player.Hp);
        Assert.Equal(43, player.Mp);
    }

    /// <summary>Verifies that player-side effect-7 invocation is refused before MP or random input changes.</summary>
    [Fact]
    public void PlayerTechnique_Dedigivolve_IsRefusedBeforeResources()
    {
        BattleState state = CreateState();
        BattleFighter player = Assert.IsType<BattleFighter>(state.ActivePlayerFighter);
        player.Id = 99;
        var rng = new CountingRng(0);
        var technique = new TechData { Accuracy = 128, Effect = BattleTechniqueEffect.Dedigivolve, EffectChance = 127, Mp = 20, Power = 0 };

        Assert.False(state.PlayerTechnique(technique, rng));

        Assert.Equal(100, player.Mp);
        Assert.Equal(99, player.Id);
        Assert.Equal(0, rng.CallCount);
    }

    /// <summary>Verifies that an enemy effect-7 invocation without an active target is refused before resources or random input change.</summary>
    [Fact]
    public void EnemyTechnique_DedigivolveWithoutActivePlayer_IsRefusedBeforeResources()
    {
        BattleState state = CreateState();
        BattleFighter enemy = Assert.IsType<BattleFighter>(state.ActiveEnemyFighter);
        state.Players[0] = null;
        state.ActivePlayer = -1;
        var rng = new CountingRng(0);
        var technique = new TechData { Accuracy = 128, Effect = BattleTechniqueEffect.Dedigivolve, EffectChance = 127, Mp = 20, Power = 0 };

        Assert.False(state.EnemyTechnique(technique, rng));

        Assert.Equal(100, enemy.Mp);
        Assert.Equal(0, rng.CallCount);
    }

    /// <summary>Verifies that an accepted forced return is queued and does not mutate before its priority transition is consumed.</summary>
    [Fact]
    public void DedigivolveTransition_AcceptedEffect_DefersMutationUntilItsPriorityEvent()
    {
        BattleState state = CreateState();
        BattleFighter player = Assert.IsType<BattleFighter>(state.ActivePlayerFighter);
        player.Id = 99;
        var scheduler = new BattleTurnScheduler();

        Assert.True(BattleDedigivolveResolver.TryQueue(BattleSide.Enemy, 0, player, [1, 3, 0x17F], 127, new SequenceRng(0), scheduler));
        Assert.Equal(99, player.Id);
        Assert.Equal(BattleScheduledEventKind.Dedigivolve, scheduler.NextKind);
        Assert.True(BattleDedigivolveResolver.ProcessNext(state, scheduler));
        Assert.Equal(1, player.Id);
    }

    /// <summary>Verifies that effect 6 immediately knocks out the active enemy at one HP and brings its living reserve into the active slot.</summary>
    [Fact]
    public void PlayerTechnique_LandedImmediateKnockOut_EliminatesOneHpEnemyAndActivatesReserve()
    {
        BattleState state = CreateState(enemyHp: 1, includeEnemyReserve: true);
        var technique = new TechData { Accuracy = 128, Effect = BattleTechniqueEffect.KnockOut, EffectChance = 127, Power = 1 };

        Assert.True(state.PlayerTechnique(technique, new SequenceRng(0, 0)));

        Assert.Equal(0, state.Enemies[0]!.Hp);
        Assert.Equal(1, state.ActiveEnemy);
        Assert.Equal(100, state.Enemies[1]!.Hp);
        Assert.Equal(BattleOutcome.None, state.Outcome);
    }

    /// <summary>Verifies that effect 6 immediately knocks out the active player from maximum HP and records defeat with no reserve.</summary>
    [Fact]
    public void EnemyTechnique_LandedImmediateKnockOut_EliminatesMaxHpPlayerAndLosesWithoutReserve()
    {
        BattleState state = CreateState();
        state.Players[1] = null;
        var technique = new TechData { Accuracy = 128, Effect = BattleTechniqueEffect.KnockOut, EffectChance = 127, Power = 1 };

        Assert.True(state.EnemyTechnique(technique, new SequenceRng(0, 0)));

        Assert.Equal(0, state.Players[0]!.Hp);
        Assert.Equal(BattleOutcome.Lost, state.Outcome);
    }

    /// <summary>Verifies that a failed effect-6 roll after a landed hit keeps the target at maximum HP.</summary>
    [Fact]
    public void PlayerTechnique_FailedImmediateKnockOutRoll_DoesNotMutateTarget()
    {
        BattleState state = CreateState();
        var target = Assert.IsType<BattleFighter>(state.ActiveEnemyFighter);
        var technique = new TechData { Accuracy = 128, Effect = BattleTechniqueEffect.KnockOut, EffectChance = 1, Power = 0 };

        Assert.True(state.PlayerTechnique(technique, new SequenceRng(0, 1)));

        Assert.Equal(target.MaxHp, target.Hp);
        Assert.Equal(BattleOutcome.None, state.Outcome);
    }

    /// <summary>Verifies that a missed effect-6 technique neither rolls immediate knock-out nor changes its target.</summary>
    [Fact]
    public void PlayerTechnique_MissedImmediateKnockOut_DoesNotMutateTarget()
    {
        BattleState state = CreateState();
        var target = Assert.IsType<BattleFighter>(state.ActiveEnemyFighter);
        var technique = new TechData { Accuracy = 0, Effect = BattleTechniqueEffect.KnockOut, EffectChance = 127, Power = 1 };
        var rng = new CountingRng(127, 0);

        Assert.True(state.PlayerTechnique(technique, rng));

        Assert.Equal(100, target.Hp);
        Assert.Equal(1, rng.CallCount);
    }

    /// <summary>Verifies that the named immediate-knock-out contract rejects absent and already-invalid targets before random input or mutation.</summary>
    [Fact]
    public void TryImmediateKnockOut_InvalidTarget_IsRefusedWithoutRandomInput()
    {
        BattleState absentTargetState = CreateState();
        absentTargetState.Enemies[0] = null;
        absentTargetState.ActiveEnemy = -1;
        var absentRng = new CountingRng(0);

        Assert.False(absentTargetState.TryImmediateKnockOut(BattleSide.Player, 127, absentRng));
        Assert.Equal(0, absentRng.CallCount);

        BattleState knockedOutTargetState = CreateState();
        var target = Assert.IsType<BattleFighter>(knockedOutTargetState.ActiveEnemyFighter);
        target.Hp = 0;
        var knockedOutRng = new CountingRng(0);

        Assert.False(knockedOutTargetState.TryImmediateKnockOut(BattleSide.Player, 127, knockedOutRng));
        Assert.Equal(0, knockedOutRng.CallCount);
        Assert.Equal(0, target.Hp);
    }

    /// <summary>Verifies that effect 6 consumes its source roll after each valid player hit, including an ordinary hit that already knocked the enemy out.</summary>
    /// <param name="enemyHp">HP of the original enemy target before the landed hit.</param>
    /// <param name="includeEnemyReserve">Whether the enemy can relay to a living reserve after the action.</param>
    /// <param name="expectedOutcome">Outcome after the player action.</param>
    [Theory]
    [InlineData(100, false, BattleOutcome.Won)]
    [InlineData(1, true, BattleOutcome.None)]
    public void PlayerTechnique_ImmediateKnockOut_ConsumesEffectRollAfterNonLethalOrLethalHit(int enemyHp, bool includeEnemyReserve, BattleOutcome expectedOutcome)
    {
        BattleState state = CreateState(enemyHp, includeEnemyReserve);
        var rng = new CountingRng(0, 0);
        var technique = new TechData { Icon = BattleConst.TechPhysical, Accuracy = 128, Effect = BattleTechniqueEffect.KnockOut, EffectChance = 127, Power = 1 };

        Assert.True(state.PlayerTechnique(technique, rng));

        Assert.Equal(1, Assert.Single(state.LastHitEvents).Damage);
        Assert.Equal(0, state.Enemies[0]!.Hp);
        Assert.Equal(expectedOutcome, state.Outcome);
        Assert.Equal(2, rng.CallCount);
        Assert.Equal(includeEnemyReserve ? 1 : -1, state.ActiveEnemy);
    }

    /// <summary>Verifies that effect 6 consumes its source roll after each valid enemy hit, including an ordinary hit that already knocked the partner out.</summary>
    /// <param name="playerHp">HP of the original player target before the landed hit.</param>
    /// <param name="includePlayerReserve">Whether the player can relay to a living reserve after the action.</param>
    /// <param name="expectedOutcome">Outcome after the enemy action.</param>
    [Theory]
    [InlineData(100, false, BattleOutcome.Lost)]
    [InlineData(1, true, BattleOutcome.None)]
    public void EnemyTechnique_ImmediateKnockOut_ConsumesEffectRollAfterNonLethalOrLethalHit(int playerHp, bool includePlayerReserve, BattleOutcome expectedOutcome)
    {
        BattleState state = CreateState();
        state.Players[0]!.Hp = playerHp;
        state.Players[1] = includePlayerReserve ? state.Players[1] : null;
        var rng = new CountingRng(0, 0);
        var technique = new TechData { Icon = BattleConst.TechPhysical, Accuracy = 128, Effect = BattleTechniqueEffect.KnockOut, EffectChance = 127, Power = 1 };

        Assert.True(state.EnemyTechnique(technique, rng));

        Assert.Equal(1, Assert.Single(state.LastHitEvents).Damage);
        Assert.Equal(0, state.Players[0]!.Hp);
        Assert.Equal(expectedOutcome, state.Outcome);
        Assert.Equal(2, rng.CallCount);
        Assert.Equal(0, state.ActivePlayer);
    }

    /// <summary>Verifies that catalogue effect 1 is explicitly accepted as an ordinary action instead of falling through the unsupported-effect default.</summary>
    [Fact]
    public void TechniqueEffect_ExplicitNoSpecialEffect_IsRecognizedWithoutMutation()
    {
        BattleState state = CreateState();
        var rng = new CountingRng(0);

        Assert.True(BattleTechniqueEffectResolver.ApplyLanded(state, BattleSide.Player, new TechData { Effect = BattleTechniqueEffect.NoSpecialEffect }, rng, 0));

        Assert.Equal(100, Assert.IsType<BattleFighter>(state.ActiveEnemyFighter).Hp);
        Assert.Equal(0, rng.CallCount);
    }

    /// <summary>Verifies that an Icon2 catalogue action follows the player hit path, spends MP, and targets the active enemy without a secondary effect.</summary>
    [Fact]
    public void PlayerTechnique_ExplicitNoSpecialPhysicalHit_ConsumesMpAndDamagesEnemy()
    {
        BattleState state = CreateState();
        var player = Assert.IsType<BattleFighter>(state.ActivePlayerFighter);
        var enemy = Assert.IsType<BattleFighter>(state.ActiveEnemyFighter);

        Assert.True(state.PlayerTechnique(PlainTechnique(BattleConst.TechPhysical, accuracy: 128, mp: 7), new SequenceRng(0)));

        Assert.Equal(93, player.Mp);
        Assert.Equal(80, enemy.Hp);
        Assert.True(Assert.Single(state.LastHitEvents).Landed);
    }

    /// <summary>Verifies that an Icon3 catalogue action follows the player magic path and spends MP on a miss without applying a secondary effect.</summary>
    [Fact]
    public void PlayerTechnique_ExplicitNoSpecialMagicMiss_ConsumesMpAndLeavesEnemyUnchanged()
    {
        BattleState state = CreateState();
        var player = Assert.IsType<BattleFighter>(state.ActivePlayerFighter);
        var enemy = Assert.IsType<BattleFighter>(state.ActiveEnemyFighter);

        Assert.True(state.PlayerTechnique(PlainTechnique(BattleConst.TechMagic, accuracy: 0, mp: 9), new SequenceRng(127)));

        Assert.Equal(91, player.Mp);
        Assert.Equal(100, enemy.Hp);
        Assert.False(Assert.Single(state.LastHitEvents).Landed);
    }

    /// <summary>Verifies that an Icon3 catalogue action follows the enemy magic path, spends enemy MP, and targets the active player.</summary>
    [Fact]
    public void EnemyTechnique_ExplicitNoSpecialMagicHit_ConsumesMpAndDamagesPlayer()
    {
        BattleState state = CreateState();
        var player = Assert.IsType<BattleFighter>(state.ActivePlayerFighter);
        var enemy = Assert.IsType<BattleFighter>(state.ActiveEnemyFighter);

        Assert.True(state.EnemyTechnique(PlainTechnique(BattleConst.TechMagic, accuracy: 128, mp: 8), new SequenceRng(0)));

        Assert.Equal(92, enemy.Mp);
        Assert.Equal(80, player.Hp);
        Assert.True(Assert.Single(state.LastHitEvents).Landed);
    }

    /// <summary>Verifies that an Icon2 catalogue action follows the enemy physical path and spends MP on a miss without touching the player.</summary>
    [Fact]
    public void EnemyTechnique_ExplicitNoSpecialPhysicalMiss_ConsumesMpAndLeavesPlayerUnchanged()
    {
        BattleState state = CreateState();
        var player = Assert.IsType<BattleFighter>(state.ActivePlayerFighter);
        var enemy = Assert.IsType<BattleFighter>(state.ActiveEnemyFighter);

        Assert.True(state.EnemyTechnique(PlainTechnique(BattleConst.TechPhysical, accuracy: 0, mp: 6), new SequenceRng(127)));

        Assert.Equal(94, enemy.Mp);
        Assert.Equal(100, player.Hp);
        Assert.False(Assert.Single(state.LastHitEvents).Landed);
    }

    /// <summary>Verifies that effect 1 rejects an icon with no source action contract before consuming MP, touching a target, or drawing random input.</summary>
    [Fact]
    public void PlayerTechnique_ExplicitNoSpecialWithUnsupportedIcon_IsRefusedBeforeResources()
    {
        BattleState state = CreateState();
        var player = Assert.IsType<BattleFighter>(state.ActivePlayerFighter);
        var enemy = Assert.IsType<BattleFighter>(state.ActiveEnemyFighter);
        var rng = new CountingRng(0);

        Assert.False(state.PlayerTechnique(PlainTechnique(icon: 5, accuracy: 128, mp: 11), rng));

        Assert.Equal(100, player.Mp);
        Assert.Equal(100, enemy.Hp);
        Assert.Equal(0, rng.CallCount);
    }

    /// <summary>Verifies that an enemy effect-1 action is refused without consuming MP or random input when its active player target is absent.</summary>
    [Fact]
    public void EnemyTechnique_ExplicitNoSpecialWithoutTarget_IsRefusedBeforeResources()
    {
        BattleState state = CreateState();
        var enemy = Assert.IsType<BattleFighter>(state.ActiveEnemyFighter);
        state.Players[0] = null;
        state.ActivePlayer = -1;
        var rng = new CountingRng(0);

        Assert.False(state.EnemyTechnique(PlainTechnique(BattleConst.TechPhysical, accuracy: 128, mp: 12), rng));

        Assert.Equal(100, enemy.Mp);
        Assert.Equal(0, rng.CallCount);
    }

    /// <summary>Verifies that an enemy-only effect spends the technique turn without resolving damage for either side.</summary>
    [Fact]
    public void EnemyOnlyTechnique_SkipsTheGenericHitSequence()
    {
        BattleState state = CreateState();
        var technique = new TechData { Accuracy = 128, Effect = BattleTechniqueEffect.EnemyOnly, Mp = 4, Power = 99 };

        Assert.True(state.PlayerTechnique(technique, new CountingRng()));

        Assert.Equal(100, state.Enemies[0]!.Hp);
        Assert.Equal(96, state.Players[0]!.Mp);
    }

    /// <summary>Creates a fabricated effect-1 technique with an explicit source icon and no secondary-effect fields.</summary>
    private static TechData PlainTechnique(int icon, int accuracy, int mp) => new()
    {
        Icon = icon,
        Accuracy = accuracy,
        Effect = BattleTechniqueEffect.NoSpecialEffect,
        Mp = mp,
        Power = 20,
    };

    private static BattleState CreateState() => CreateState(enemyHp: 100, includeEnemyReserve: false);

    private static BattleState CreateState(int enemyHp, bool includeEnemyReserve)
    {
        var stats = new BattleStats();
        stats.Stats[BattleStat.Attack] = 20;
        stats.Stats[BattleStat.Defense] = 20;
        stats.Stats[BattleStat.Spirit] = 20;
        stats.Stats[BattleStat.Speed] = 100;
        var setup = new BattleSetup();
        setup.StarterDigimonIds = [1, 3];
        setup.Partners[0] = new PartnerSeed { Id = 1, Hp = 100, MaxHp = 100, Mp = 100, MaxMp = 100, Stats = stats };
        setup.Partners[1] = new PartnerSeed { Id = 3, Hp = 100, MaxHp = 100, Mp = 100, MaxMp = 100, Stats = stats };
        setup.Enemies[0] = new BattleEnemy { Fighter = 2, Hp = enemyHp, Mp = 100, Strength = 16, BaseStats = stats };
        setup.Enemies[1] = includeEnemyReserve ? new BattleEnemy { Fighter = 4, Hp = 100, Mp = 100, Strength = 16, BaseStats = stats } : null;
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
