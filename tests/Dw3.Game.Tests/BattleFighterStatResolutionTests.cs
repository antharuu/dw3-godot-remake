using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests;

/// <summary>Tests live battle-stat resolution from a fighter's mutable state.</summary>
public sealed class BattleFighterStatResolutionTests
{
    /// <summary>Verifies that runtime modifiers are recomputed instead of retaining the seed snapshot.</summary>
    [Fact]
    public void PartnerStatsReflectCurrentChargeFlagsAndBoosts()
    {
        var baseStats = new BattleStats { Family = 2 };
        baseStats.Stats[BattleStat.Attack] = 50;
        baseStats.Stats[BattleStat.Defense] = 60;
        baseStats.Stats[BattleStat.Spirit] = 70;
        baseStats.Stats[BattleStat.Speed] = 80;
        baseStats.Resist[0] = 100;
        var seed = new PartnerSeed { Id = 1, Stats = baseStats };
        BattleFighter fighter = BattleFighter.FromPartner(seed, id: 1);

        fighter.Charge = 64;
        fighter.Flags = FighterFlags.NoSwitch;
        fighter.Boosts[0] = 7;
        fighter.Boosts[1] = -100;
        fighter.Boosts[2] = 9;
        fighter.Boosts[3] = 11;
        fighter.RefreshStats();

        BattleStats resolved = fighter.Stats;

        Assert.Equal(FighterFlags.NoSwitch, resolved.Flags);
        Assert.Equal(64, resolved.DamageBonus);
        Assert.Equal(57, resolved.Stats[BattleStat.Attack]);
        Assert.Equal(1, resolved.Stats[BattleStat.Defense]);
        Assert.Equal(81, resolved.Stats[BattleStat.Spirit]);
        Assert.Equal(89, resolved.Stats[BattleStat.Speed]);
        Assert.Equal(50, baseStats.Stats[BattleStat.Attack]);
    }

    /// <summary>Verifies that a transformed partner selects its own persistent form source at damage resolution time.</summary>
    [Fact]
    public void PartnerStatsSelectCurrentFormSource()
    {
        var baseStats = new BattleStats();
        baseStats.Stats[BattleStat.Attack] = 10;
        var formStats = new BattleStats();
        formStats.Stats[BattleStat.Attack] = 90;
        var seed = new PartnerSeed
        {
            Id = 1,
            Stats = baseStats,
            FormStats = new Dictionary<int, BattleStats> { [2] = formStats },
        };
        BattleFighter fighter = BattleFighter.FromPartner(seed, id: 1);
        fighter.Id = 2;

        fighter.RefreshStats();

        Assert.Equal(90, fighter.Stats.Stats[BattleStat.Attack]);
        Assert.Equal(10, baseStats.Stats[BattleStat.Attack]);
    }
}
