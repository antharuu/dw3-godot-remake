using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests;

/// <summary>Verifies the healing item techniques a fighter uses as its own technique, for both sides.</summary>
public sealed class BattleTechniqueRecoveryTests
{
    /// <summary>Verifies the active heal: 64 HP per power plus power/8 per wisdom point, MP spent, charge spent, turn spent, no hit rolled.</summary>
    [Fact]
    public void PlayerTechnique_ActiveHeal_HealsUserBySourceFormula()
    {
        BattleState state = CreateState(wisdom: 40);
        BattleFighter user = Assert.IsType<BattleFighter>(state.ActivePlayerFighter);
        user.Hp = 100;
        user.Charge = 32;

        bool spent = state.PlayerTechnique(Heal(0xB8, power: 8, mp: 16), new SequenceRng());

        Assert.True(spent);
        Assert.Equal(100 + (8 * 64) + (40 * 8 / 8), user.Hp);
        Assert.Equal(984, user.Mp);
        Assert.Equal(0, user.Charge);
        Assert.Empty(state.LastHitEvents);
        BattleRecoveryOutcome recovery = Assert.IsType<BattleRecoveryOutcome>(state.LastRecovery);
        Assert.Equal((BattleSide.Player, false, 552), (recovery.Side, recovery.SideWide, recovery.Amount));
        Assert.Equal([1], recovery.NamedIds);
    }

    /// <summary>Verifies that an active heal prints only the HP actually missing and caps the user at its maximum.</summary>
    [Fact]
    public void PlayerTechnique_ActiveHeal_PrintsTheMissingHpOnly()
    {
        BattleState state = CreateState(wisdom: 0);
        BattleFighter user = Assert.IsType<BattleFighter>(state.ActivePlayerFighter);
        user.Hp = 990;

        Assert.True(state.PlayerTechnique(Heal(0xB9, power: 32, mp: 74), new SequenceRng()));

        Assert.Equal(1000, user.Hp);
        Assert.Equal(10, Assert.IsType<BattleRecoveryOutcome>(state.LastRecovery).Amount);
    }

    /// <summary>Verifies the side heal: every living partner gains the heal, the knocked-out one stays down, and the message names the hurt ones with the largest missing HP capped by the heal.</summary>
    [Fact]
    public void PlayerTechnique_SideHeal_HealsLivingSideAndNamesHurtFighters()
    {
        BattleState state = CreateState(wisdom: 0);
        BattleFighter first = Assert.IsType<BattleFighter>(state.Players[0]);
        BattleFighter second = Assert.IsType<BattleFighter>(state.Players[1]);
        BattleFighter third = Assert.IsType<BattleFighter>(state.Players[2]);
        first.Hp = 900;
        second.Hp = 0;
        third.Hp = 1000;

        Assert.True(state.PlayerTechnique(Heal(0xBB, power: 32, mp: 188), new SequenceRng()));

        Assert.Equal((1000, 0, 1000), (first.Hp, second.Hp, third.Hp));
        BattleRecoveryOutcome recovery = Assert.IsType<BattleRecoveryOutcome>(state.LastRecovery);
        Assert.True(recovery.SideWide);
        Assert.Equal(100, recovery.Amount);
        Assert.Equal([1], recovery.NamedIds);
    }

    /// <summary>Verifies that a heal without anything to restore still spends the turn and MP and records a no-effect result naming the user.</summary>
    [Fact]
    public void PlayerTechnique_HealAtFullHp_SpendsTurnWithNoEffect()
    {
        BattleState state = CreateState(wisdom: 0);
        BattleFighter user = Assert.IsType<BattleFighter>(state.ActivePlayerFighter);

        Assert.True(state.PlayerTechnique(Heal(0xBA, power: 127, mp: 120), new SequenceRng()));

        Assert.Equal((1000, 880), (user.Hp, user.Mp));
        BattleRecoveryOutcome recovery = Assert.IsType<BattleRecoveryOutcome>(state.LastRecovery);
        Assert.False(recovery.Healed);
        Assert.Equal([1], recovery.NamedIds);
    }

    /// <summary>Verifies the source cap of 9999 HP per heal.</summary>
    [Fact]
    public void ComputeHeal_CapsAt9999()
    {
        var stats = new BattleStats();
        stats.Stats[BattleStat.Wisdom] = 999;

        Assert.Equal(9999, BattleTechniqueRecovery.ComputeHeal(Heal(0xBA, power: 127, mp: 0), stats));
    }

    /// <summary>Verifies that only the source heal ids with the item icon belong to the family.</summary>
    [Theory]
    [InlineData(0xB8, 4, 15, true)]
    [InlineData(0xBC, 4, 15, true)]
    [InlineData(0xB7, 4, 15, false)]
    [InlineData(0xBD, 4, 15, false)]
    [InlineData(0xB8, 3, 15, false)]
    [InlineData(0xB8, 4, 16, false)]
    public void Supports_OnlyItemIconHealIds(int id, int icon, int effect, bool expected)
    {
        var technique = Heal(id, power: 8, mp: 0);
        technique.Icon = icon;
        technique.Effect = effect;

        Assert.Equal(expected, BattleTechniqueRecovery.Supports(technique));
    }

    /// <summary>Verifies that the enemy uses the same heal on its own active fighter, paying its MP.</summary>
    [Fact]
    public void EnemyTechnique_ActiveHeal_HealsTheEnemy()
    {
        BattleState state = CreateState(wisdom: 0);
        BattleFighter enemy = Assert.IsType<BattleFighter>(state.ActiveEnemyFighter);
        enemy.Hp = 50;

        Assert.True(state.EnemyTechnique(Heal(0xB8, power: 8, mp: 16), new SequenceRng()));

        Assert.Equal((562, 84), (enemy.Hp, enemy.Mp));
        BattleRecoveryOutcome recovery = Assert.IsType<BattleRecoveryOutcome>(state.LastRecovery);
        Assert.Equal((BattleSide.Enemy, 512, 3), (recovery.Side, recovery.Amount, recovery.NamedIds[0]));
    }

    /// <summary>Verifies that a heal following an attack does not keep the attack's hits for its own result.</summary>
    [Fact]
    public void PlayerTechnique_HealAfterAttack_ClearsThePreviousHits()
    {
        BattleState state = CreateState(wisdom: 0);
        var attack = new TechData { Id = 1, Power = 10, Icon = BattleConst.TechPhysical, Accuracy = 100, Script = 1 };
        Assert.True(state.PlayerAttack(attack, new SequenceRng()));
        Assert.NotEmpty(state.LastHitEvents);
        Assert.IsType<BattleFighter>(state.ActivePlayerFighter).Hp = 500;

        Assert.True(state.PlayerTechnique(Heal(0xB8, power: 8, mp: 16), new SequenceRng()));

        Assert.Empty(state.LastHitEvents);
        Assert.Equal(500, Assert.IsType<BattleRecoveryOutcome>(state.TakeLastRecovery()).Amount);
        Assert.Null(state.LastRecovery);
    }

    /// <summary>Verifies the result pages of each heal outcome.</summary>
    [Fact]
    public void ResultPages_ShowTheHealOutcome()
    {
        Assert.Equal(("recovered", 7, 552), Page(new BattleRecoveryOutcome(BattleSide.Player, false, [7], 552)));
        Assert.Equal(("sideRecovered", 7, 90), Page(new BattleRecoveryOutcome(BattleSide.Player, true, [7, 8], 90)));
        Assert.Equal(("noEffect", 7, 0), Page(new BattleRecoveryOutcome(BattleSide.Player, true, [7], 0)));
        var side = Assert.Single(BattleActionResultPages.Build(Context(new BattleRecoveryOutcome(BattleSide.Enemy, true, [7, 8], 90))));
        Assert.Equal([7, 8], side.NamedIds);
        Assert.Equal(BattleSide.Enemy, side.TargetSide);
    }

    private static (string Key, int Target, int Amount) Page(BattleRecoveryOutcome recovery)
    {
        BattleActionResultPage page = Assert.Single(BattleActionResultPages.Build(Context(recovery)));
        return (page.Key, page.TargetId, page.Amount);
    }

    private static ActionSequenceContext Context(BattleRecoveryOutcome recovery) =>
        new(recovery.Side, 7, 20, [], 10) { Recovery = recovery };

    private static TechData Heal(int id, int power, int mp) => new()
    {
        Id = id,
        Mp = mp,
        Power = power,
        Icon = BattleConst.TechItem,
        Kind = id >= 0xBB ? 4 : 3,
        Accuracy = 100,
        Effect = BattleTechniqueEffect.ItemHeal,
        Script = 10,
    };

    private static BattleState CreateState(int wisdom)
    {
        var setup = new BattleSetup();
        setup.Partners[0] = Partner(1, wisdom);
        setup.Partners[1] = Partner(2, wisdom);
        setup.Partners[2] = Partner(4, wisdom);
        setup.Enemies[0] = new BattleEnemy { Fighter = 3, Hp = 1000, Mp = 100, Strength = 16, BaseStats = new BattleStats() };
        return BattleState.InitFighters(setup, 1, new SequenceRng(127));
    }

    private static PartnerSeed Partner(int id, int wisdom)
    {
        var stats = new BattleStats();
        stats.Stats[BattleStat.Wisdom] = wisdom;
        return new PartnerSeed { Id = id, Hp = 1000, MaxHp = 1000, Mp = 1000, MaxMp = 1000, Stats = stats };
    }

    private sealed class SequenceRng(params int[] values) : IBattleRng
    {
        private readonly Queue<int> _values = new(values);

        public int NextValue() => _values.Count > 0 ? _values.Dequeue() : 0;
    }
}
