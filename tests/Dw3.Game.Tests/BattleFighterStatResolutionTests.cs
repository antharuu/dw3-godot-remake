using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests;

/// <summary>Tests live battle-stat resolution from a fighter's mutable state.</summary>
public sealed class BattleFighterStatResolutionTests
{
    // ── Enemy (FromEnemy factory) regression: boost stacking ────────────

    /// <summary>
    /// Verifies that repeated <see cref="BattleFighter.RefreshStats"/> calls with the same boost
    /// do not accumulate the boost on each call. The original defect was that the enemy path used
    /// <c>Stats</c> as the resolution source rather than the persistent <c>_baseStats</c>, so
    /// each refresh added the boost to the previous already-boosted snapshot.
    /// </summary>
    [Fact]
    public void EnemyRefreshStatsDoesNotStackBoostsOnRepeatedCalls()
    {
        BattleFighter fighter = MakeEnemyWithAttack(50);
        fighter.Boosts[0] = 7;

        fighter.RefreshStats();
        int firstAttack = fighter.Stats.Stats[BattleStat.Attack];

        fighter.RefreshStats();
        int secondAttack = fighter.Stats.Stats[BattleStat.Attack];

        Assert.Equal(57, firstAttack);
        Assert.Equal(firstAttack, secondAttack);
    }

    /// <summary>
    /// Verifies that clearing a boost and refreshing restores the persistent base value rather
    /// than the last boosted snapshot value.
    /// </summary>
    [Fact]
    public void EnemyRefreshStatsRestoresBaseAfterBoostCleared()
    {
        BattleFighter fighter = MakeEnemyWithAttack(50);
        fighter.Boosts[0] = 7;
        fighter.RefreshStats();
        fighter.RefreshStats();

        fighter.Boosts[0] = 0;
        fighter.RefreshStats();

        Assert.Equal(50, fighter.Stats.Stats[BattleStat.Attack]);
    }

    /// <summary>
    /// Verifies that a negative boost is clamped to 1 on the snapshot and that clearing it
    /// restores the persistent base value.
    /// </summary>
    [Fact]
    public void EnemyNegativeBoostClampsAndDoesNotMutateBase()
    {
        BattleFighter fighter = MakeEnemyWithAttack(50);
        fighter.Boosts[0] = -100;
        fighter.RefreshStats();

        int clamped = fighter.Stats.Stats[BattleStat.Attack];

        fighter.Boosts[0] = 0;
        fighter.RefreshStats();

        int restored = fighter.Stats.Stats[BattleStat.Attack];

        Assert.Equal(1, clamped);
        Assert.Equal(50, restored);
    }

    /// <summary>
    /// Verifies that repeated refreshes with no boost leave the attack stat unchanged (neutral case).
    /// </summary>
    [Fact]
    public void EnemyRefreshStatsIsIdempotentWithNoBoost()
    {
        BattleFighter fighter = MakeEnemyWithAttack(50);

        fighter.RefreshStats();
        int first = fighter.Stats.Stats[BattleStat.Attack];
        fighter.RefreshStats();
        int second = fighter.Stats.Stats[BattleStat.Attack];

        Assert.Equal(50, first);
        Assert.Equal(50, second);
    }

    /// <summary>
    /// Verifies that the persistent base stats source (the enemy entry after scaling) is never
    /// mutated by any number of <see cref="BattleFighter.RefreshStats"/> calls.
    /// </summary>
    [Fact]
    public void EnemyRefreshStatsNeverMutatesBaseStatSource()
    {
        var baseEntry = new BattleStats();
        baseEntry.Stats[BattleStat.Attack] = 50;
        var enemy = new BattleEnemy { Fighter = 1, Level = 25, Hp = 500, Mp = 100, Strength = 16, BaseStats = baseEntry };
        BattleFighter fighter = BattleFighter.FromEnemy(enemy);

        int attackAfterFactory = baseEntry.Stats[BattleStat.Attack];
        fighter.Boosts[0] = 7;
        fighter.RefreshStats();
        fighter.RefreshStats();

        Assert.Equal(50, attackAfterFactory);
        Assert.Equal(50, baseEntry.Stats[BattleStat.Attack]);
    }

    // ── Partner (FromPartner factory) ────────────────────────────────────

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

    // ── Helpers ──────────────────────────────────────────────────────────
    private static BattleFighter MakeEnemyWithAttack(int attack)
    {
        var baseEntry = new BattleStats();
        baseEntry.Stats[BattleStat.Attack] = attack;
        var enemy = new BattleEnemy
        {
            Fighter = 1,
            Level = 25,
            Hp = 500,
            Mp = 100,
            Strength = 16,
            BaseStats = baseEntry,
        };
        return BattleFighter.FromEnemy(enemy);
    }
}
