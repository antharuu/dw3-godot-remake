using Dw3Production.Session;
using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests;

/// <summary>Verifies blast digivolution gauge progression, trigger, duration and return rules (B9-041).</summary>
public sealed class BattleBlastTests
{
    private const int BaseFormId = 383;
    private const int BlastFormId = 392;
    private const int OtherFormId = 373;

    // ── gauge rules ──────────────────────────────────────────────────────────

    /// <summary>Oracle B9-076: damage 22 → gain 2 (floor division by 10).</summary>
    [Theory]
    [InlineData(9, 0)]
    [InlineData(10, 1)]
    [InlineData(22, 2)]
    [InlineData(109, 10)]
    public void GaugeGain_FloorDividesBy10(int damage, int expectedGain)
    {
        Assert.Equal(expectedGain, BattleBlastGaugeRules.ComputeGain(damage));
    }

    /// <summary>Gauge never exceeds the trigger threshold after clamping.</summary>
    [Fact]
    public void GaugeClamp_CapsAtThreshold()
    {
        Assert.Equal(1000, BattleBlastGaugeRules.ClampGauge(1001));
        Assert.Equal(1000, BattleBlastGaugeRules.ClampGauge(1000));
        Assert.Equal(999, BattleBlastGaugeRules.ClampGauge(999));
    }

    /// <summary>Trigger fires only when the previous value was below the threshold and new value meets it.</summary>
    [Fact]
    public void IsTriggered_OnlyOnFirstCrossing()
    {
        Assert.True(BattleBlastGaugeRules.IsTriggered(999, 1000));
        Assert.False(BattleBlastGaugeRules.IsTriggered(1000, 1000));
        Assert.False(BattleBlastGaugeRules.IsTriggered(998, 999));
    }

    // ── tier selection ───────────────────────────────────────────────────────

    /// <summary>Oracle B9-076: level 25 and boundaries [4,19,39,70] → tier 2.</summary>
    [Fact]
    public void SelectTier_Level25_ReturnsTier2()
    {
        IReadOnlyList<int> tiers = [4, 19, 39, 70];
        Assert.Equal(2, BattleBlastGaugeRules.SelectTier(tiers, 25));
    }

    /// <summary>Level below the first boundary maps to tier 0.</summary>
    [Fact]
    public void SelectTier_LevelBelowFirstBoundary_ReturnsTier0()
    {
        IReadOnlyList<int> tiers = [4, 19, 39, 70];
        Assert.Equal(0, BattleBlastGaugeRules.SelectTier(tiers, 3));
    }

    /// <summary>Level above all boundaries maps to tier count (highest).</summary>
    [Fact]
    public void SelectTier_LevelAboveAllBoundaries_ReturnsMaxTier()
    {
        IReadOnlyList<int> tiers = [4, 19, 39, 70];
        Assert.Equal(4, BattleBlastGaugeRules.SelectTier(tiers, 100));
    }

    /// <summary>Each tier maps to its fixed blast delay family in ascending duration order.</summary>
    [Theory]
    [InlineData(0, BattleActionDelayKind.BlastTwoSeconds)]
    [InlineData(1, BattleActionDelayKind.BlastTwoAndHalfSeconds)]
    [InlineData(2, BattleActionDelayKind.BlastThreeSeconds)]
    [InlineData(3, BattleActionDelayKind.BlastThreeAndHalfSeconds)]
    [InlineData(4, BattleActionDelayKind.BlastFourSeconds)]
    [InlineData(99, BattleActionDelayKind.BlastFourSeconds)]
    public void DelayKindForTier_MapsExpectedFamilies(int tier, BattleActionDelayKind expected)
    {
        Assert.Equal(expected, BattleBlastGaugeRules.DelayKindForTier(tier));
    }

    // ── oracle chain trigger ─────────────────────────────────────────────────

    /// <summary>Oracle B9-076 checkpoint 992: gauge 999 + damage 22 → gain 2 → clamp 1000 → blast fires, form 383→392, temporary=1.</summary>
    [Fact]
    public void PlayerAttack_OracleChain_GaugeChargesAndBlastFires()
    {
        BattleState state = CreateBlastState(gaugePreset: 999, playerMaxHp: 300);
        var player = Assert.IsType<BattleFighter>(state.ActivePlayerFighter);

        Assert.Equal(BaseFormId, player.Id);
        Assert.Equal(999, player.BlastGauge);
        state.PlayerAttack(AttackTech(power: 22), new FixedAccurateRng());

        Assert.Equal(BlastFormId, player.Id);
        Assert.True(player.Temporary);
        Assert.Equal(BaseFormId, player.PrevId);
        Assert.Equal(0, player.BlastGauge);
    }

    /// <summary>Gauge below threshold after a hit: blast does not fire and gauge increases.</summary>
    [Fact]
    public void PlayerAttack_GaugeBelowThreshold_NoBlast()
    {
        BattleState state = CreateBlastState(gaugePreset: 0, playerMaxHp: 300);
        var player = Assert.IsType<BattleFighter>(state.ActivePlayerFighter);

        state.PlayerAttack(AttackTech(power: 22), new FixedAccurateRng());

        Assert.Equal(BaseFormId, player.Id);
        Assert.False(player.Temporary);
        Assert.True(player.BlastGauge > 0, "gauge should have increased");
        Assert.True(player.BlastGauge < 1000, "gauge should not have reached the threshold");
    }

    // ── single application ───────────────────────────────────────────────────

    /// <summary>Blast does not re-trigger while the partner is already in a temporary blast form.</summary>
    [Fact]
    public void Blast_DoesNotRetriggerWhileTemporary()
    {
        BattleState state = CreateBlastState(gaugePreset: 999, playerMaxHp: 300);
        var player = Assert.IsType<BattleFighter>(state.ActivePlayerFighter);

        state.PlayerAttack(AttackTech(power: 22), new FixedAccurateRng());
        Assert.True(player.Temporary);
        int formAfterBlast = player.Id;

        AdvanceEnemyTurn(state);
        state.PlayerAttack(AttackTech(power: 100), new FixedAccurateRng());

        Assert.Equal(formAfterBlast, player.Id);
        Assert.True(player.Temporary);
        Assert.Equal(0, player.BlastGauge);
    }

    // ── blast return after duration ──────────────────────────────────────────

    /// <summary>After the blast expiry fires, the partner returns to PrevId and clears Temporary.</summary>
    [Fact]
    public void Blast_AfterDuration_ReturnsToPrevId()
    {
        BattleState state = CreateBlastState(gaugePreset: 999, playerMaxHp: 300);
        var player = Assert.IsType<BattleFighter>(state.ActivePlayerFighter);

        state.PlayerAttack(AttackTech(power: 22), new FixedAccurateRng());
        Assert.True(player.Temporary);

        DrainBlastReturn(state);

        Assert.Equal(BaseFormId, player.Id);
        Assert.False(player.Temporary);
        Assert.Equal(0, player.BlastGauge);
    }

    /// <summary>When a partner was in a non-origin form before blast, return restores that exact pre-blast form (not the seed origin).</summary>
    [Fact]
    public void Blast_ReturnFromNonOriginForm_RestoresPreBlastForm()
    {
        BattleState state = CreateBlastState(gaugePreset: 999, playerMaxHp: 300, startFormId: OtherFormId);
        var player = Assert.IsType<BattleFighter>(state.ActivePlayerFighter);

        Assert.Equal(OtherFormId, player.Id);
        state.PlayerAttack(AttackTech(power: 22), new FixedAccurateRng());
        Assert.Equal(OtherFormId, player.PrevId);
        Assert.Equal(BlastFormId, player.Id);

        DrainBlastReturn(state);

        Assert.Equal(OtherFormId, player.Id);
        Assert.False(player.Temporary);
    }

    // ── battle exit commit ───────────────────────────────────────────────────

    /// <summary>CommitBattle with an active blast form commits the pre-blast form id and resets the gauge.</summary>
    [Fact]
    public void CommitBattle_BlastActiveAtBattleEnd_CommitsPreBlastFormAndZeroGauge()
    {
        BattleState state = CreateBlastState(gaugePreset: 999, playerMaxHp: 300);
        var player = Assert.IsType<BattleFighter>(state.ActivePlayerFighter);
        state.PlayerAttack(AttackTech(power: 22), new FixedAccurateRng());
        Assert.True(player.Temporary);

        var seed = new PartnerSeed { Id = BaseFormId, Hp = 300, MaxHp = 300, BlastGauge = 999 };
        var partner = new PlayerSessionPartner(seed, [BaseFormId], [], [], []);
        var session = new PlayerSession([partner], new Dictionary<int, int>());
        session.CommitBattle([player]);

        Assert.Equal(BaseFormId, seed.Id);
        Assert.Equal(0, seed.BlastGauge);
    }

    /// <summary>CommitBattle without blast commits the current form and resets the gauge.</summary>
    [Fact]
    public void CommitBattle_NormalFormAtBattleEnd_CommitsCurrentFormAndZeroGauge()
    {
        var seed = new PartnerSeed { Id = BaseFormId, Hp = 100, MaxHp = 100, BlastGauge = 500 };
        var partner = new PlayerSessionPartner(seed, [BaseFormId, OtherFormId], [OtherFormId], [], []);
        var session = new PlayerSession([partner], new Dictionary<int, int>());

        var fighter = BattleFighter.FromPartner(seed, OtherFormId);
        fighter.Hp = 80;
        session.CommitBattle([fighter]);

        Assert.Equal(OtherFormId, seed.Id);
        Assert.Equal(0, seed.BlastGauge);
    }

    // ── blast form stats ─────────────────────────────────────────────────────

    /// <summary>When FormStats has an entry for the blast form, its stats apply after the trigger.</summary>
    [Fact]
    public void Blast_AppliesBlastFormStatsFromFormStats()
    {
        var blastStats = new BattleStats();
        blastStats.Stats[BattleStat.Attack] = 99;
        blastStats.Stats[BattleStat.Defense] = 1;
        blastStats.Stats[BattleStat.Speed] = 100;

        BattleState state = CreateBlastState(gaugePreset: 999, playerMaxHp: 300, blastFormStats: blastStats);
        var player = Assert.IsType<BattleFighter>(state.ActivePlayerFighter);

        state.PlayerAttack(AttackTech(power: 22), new FixedAccurateRng());

        Assert.True(player.Temporary);
        Assert.Equal(99, player.Stats.Stats[BattleStat.Attack]);
    }

    // ── no catalogue entry: gauge saturates but blast does not fire ──────────

    /// <summary>When no blast form is registered for the fighter's id, the gauge reaches the threshold but no transformation occurs.</summary>
    [Fact]
    public void GaugeReachesThreshold_NoCatalogueEntry_NoBlast()
    {
        BattleState state = CreateBlastState(gaugePreset: 999, playerMaxHp: 300, blastFormsById: new Dictionary<int, int>());
        var player = Assert.IsType<BattleFighter>(state.ActivePlayerFighter);

        state.PlayerAttack(AttackTech(power: 22), new FixedAccurateRng());

        Assert.Equal(BaseFormId, player.Id);
        Assert.False(player.Temporary);
        Assert.Equal(1000, player.BlastGauge);
    }

    // ── seed → fighter gauge propagation ────────────────────────────────────

    /// <summary>A partner seed with a non-zero gauge propagates it to the fighter on FromPartner.</summary>
    [Fact]
    public void GaugeCarriesOver_FromSeedToFighter()
    {
        var seed = new PartnerSeed { Id = BaseFormId, Hp = 100, MaxHp = 100, Mp = 10, MaxMp = 10, BlastGauge = 500 };
        var fighter = BattleFighter.FromPartner(seed, BaseFormId);

        Assert.Equal(500, fighter.BlastGauge);
    }

    // ── partner identity preserved across slots ──────────────────────────────

    /// <summary>Blast on slot 0 does not affect the gauge or form of a living slot 1 partner.</summary>
    [Fact]
    public void Blast_SlotIdentity_DoesNotAffectOtherPartnerSlots()
    {
        BattleState state = CreateBlastState(gaugePreset: 999, playerMaxHp: 300);
        var slot1 = state.Players[1];
        int slot1FormBefore = slot1?.Id ?? 0;
        int slot1GaugeBefore = slot1?.BlastGauge ?? 0;

        state.PlayerAttack(AttackTech(power: 22), new FixedAccurateRng());

        Assert.Equal(slot1FormBefore, slot1?.Id ?? 0);
        Assert.Equal(slot1GaugeBefore, slot1?.BlastGauge ?? 0);
    }

    // ── helpers ──────────────────────────────────────────────────────────────
    private static BattleState CreateBlastState(
        int gaugePreset,
        int playerMaxHp,
        int startFormId = BaseFormId,
        BattleStats? blastFormStats = null,
        IReadOnlyDictionary<int, int>? blastFormsById = null)
    {
        var playerStats = new BattleStats();
        playerStats.Stats[BattleStat.Attack] = 20;
        playerStats.Stats[BattleStat.Defense] = 1;
        playerStats.Stats[BattleStat.Speed] = 100;
        playerStats.Level = 25;

        var formStats = new Dictionary<int, BattleStats>();
        if (blastFormStats is not null)
        {
            formStats[BlastFormId] = blastFormStats;
        }

        var seed = new PartnerSeed
        {
            Id = BaseFormId,
            Hp = playerMaxHp,
            MaxHp = playerMaxHp,
            Mp = 100,
            MaxMp = 100,
            BlastGauge = gaugePreset,
            Stats = playerStats,
            FormStats = formStats,
        };
        var reserve = new PartnerSeed { Id = OtherFormId, Hp = 100, MaxHp = 100, Stats = playerStats };

        var enemyStats = new BattleStats();
        enemyStats.Stats[BattleStat.Attack] = 1;
        enemyStats.Stats[BattleStat.Defense] = 1;
        enemyStats.Stats[BattleStat.Speed] = 100;

        var setup = new BattleSetup();
        setup.StarterDigimonIds = [BaseFormId];
        setup.Partners[0] = seed;
        setup.Partners[1] = reserve;
        setup.Enemies[0] = new BattleEnemy { Fighter = 99, Hp = 1000, Mp = 100, Strength = 16, BaseStats = enemyStats };

        var state = BattleState.InitFighters(setup, startFormId, new FixedAccurateRng());
        state.BlastFormsById = blastFormsById ?? new Dictionary<int, int>
        {
            [BaseFormId] = BlastFormId,
            [OtherFormId] = BlastFormId,
        };
        return state;
    }

    private static TechData AttackTech(int power) => new()
    {
        Accuracy = 128,
        Power = power,
        HitCount = 1,
    };

    private static void AdvanceEnemyTurn(BattleState state)
    {
        var missAll = new TechData { Accuracy = 0, Power = 1, HitCount = 1 };
        state.EnemyAttack(missAll, new FixedAccurateRng());
    }

    private static void DrainBlastReturn(BattleState state)
    {
        var missTech = new TechData { Accuracy = 0, Power = 0, HitCount = 1 };
        for (int i = 0; i < 100; i++)
        {
            AdvanceEnemyTurn(state);
            state.PlayerAttack(missTech, new FixedAccurateRng());
            if (state.ActivePlayerFighter is not { Temporary: true })
            {
                return;
            }
        }
    }

    private sealed class FixedAccurateRng : IBattleRng
    {
        public int NextValue() => 0;
    }
}
