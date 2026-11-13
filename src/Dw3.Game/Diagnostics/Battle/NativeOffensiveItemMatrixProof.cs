using Dw3Production.Diagnostics.Field;
using Dw3Production.Simulation;
using Godot;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Native host matrix for the remaining enemy-targeted item families. Every precondition is SYNTHETIC and
/// mirrors a qualified B9-046c vector, while menu selection, the item effect, scheduling and expiry run production code.</summary>
internal static class NativeOffensiveItemMatrixProof
{
    /// <summary>Maximum ordinary steps allowed before a scheduled expiry must have been consumed.</summary>
    private const int ExpirySteps = 96;

    /// <summary>Runs every enemy-targeted family case.</summary>
    public static void Run()
    {
        Drain();
        Confuse();
        WeakenDefense();
        LowerStat();
        LowerSpeed();
        GD.Print("NATIVE_ROOT_MENU_PROOF PASS item-matrix=drain-confuse-weakenDefense-lowerStat-lowerSpeed fixture=SYNTHETIC ingress=GameHost._Input");
    }

    /// <summary>Drain moves a fifth of the enemy maximum HP to the partner, immediately and without draws.</summary>
    private static void Drain()
    {
        using var session = new NativeMenuSession(includeOffensiveItems: true);
        BattleFighter player = session.Player;
        BattleFighter enemy = session.Enemy;
        player.Hp = 40;
        enemy.MaxHp = 100;
        enemy.Hp = 90;
        string before = $"player-hp={player.Hp} enemy-hp={enemy.Hp}/{enemy.MaxHp}";
        string action = NativeTurnDriver.UseItemObserved(session, 0x55, "[]");
        ProbeRequirement.Require(player.Hp == 60 && enemy.Hp == 70, $"drain vector 40/90 -> 60/70 failed {player.Hp}/{enemy.Hp} {action}");
        NativeTurnDriver.Report("drain", 0x55, before, $"player-hp={player.Hp} enemy-hp={enemy.Hp}", action, "immediate=no-expiry");
    }

    /// <summary>Confusion succeeds on an odd draw, backfires on a zero quarter draw, then both expire through the status consumer.</summary>
    private static void Confuse()
    {
        using var session = new NativeMenuSession(includeOffensiveItems: true, fixture: new(RngScript: [127, 0]));
        BattleFighter player = session.Player;
        BattleFighter enemy = session.Enemy;
        string before = $"enemy-confused={Confused(enemy)} player-confused={Confused(player)}";
        string action = NativeTurnDriver.UseItemObserved(session, 0x56, "[127,0]");
        ProbeRequirement.Require(Confused(enemy) && Confused(player) && enemy.Confusion == 96 && player.Confusion == 96, $"confusion vector 96/96 with backfire failed {enemy.Confusion}/{player.Confusion}");
        string after = $"enemy-confusion={enemy.Confusion} player-confusion={player.Confusion}";
        int steps = NativeTurnDriver.AdvanceUntil(session, () => !Confused(enemy) && !Confused(player), ExpirySteps);
        NativeTurnDriver.Report("confuse", 0x56, before, after, action, $"expired-after-steps={steps}");
    }

    /// <summary>Weakening raises attack by 3/10 and halves defense from resolved stats, then both exact modifiers expire.</summary>
    private static void WeakenDefense()
    {
        using var session = new NativeMenuSession(includeOffensiveItems: true, fixture: new(setup => SetEnemyBase(setup, 100, 100, 1)));
        BattleFighter enemy = session.Enemy;
        string before = Boosts(enemy);
        string action = NativeTurnDriver.UseItemObserved(session, 0x57, "[0,0]");
        ProbeRequirement.Require(enemy.Boosts[0] == 30 && enemy.Boosts[1] == -50, $"weaken vector 30/-50 failed {Boosts(enemy)}");
        string after = Boosts(enemy);
        int steps = NativeTurnDriver.AdvanceUntil(session, () => enemy.Boosts[0] == 0 && enemy.Boosts[1] == 0, ExpirySteps);
        NativeTurnDriver.Report("weakenDefense", 0x57, before, after, action, $"expired-after-steps={steps}");
    }

    /// <summary>The equalizer removes the resolved attack excess over defense, then its exact modifier expires.</summary>
    private static void LowerStat()
    {
        using var session = new NativeMenuSession(includeOffensiveItems: true, fixture: new(setup => SetEnemyBase(setup, 100, 80, 1)));
        BattleFighter enemy = session.Enemy;
        string before = Boosts(enemy);
        string action = NativeTurnDriver.UseItemObserved(session, 0x58, "[0]");
        enemy.RefreshStats();
        ProbeRequirement.Require(enemy.Boosts[0] == -20 && enemy.Stats.Stats[BattleStat.Attack] == enemy.Stats.Stats[BattleStat.Defense], $"equalizer vector -20 failed {Boosts(enemy)}");
        string after = Boosts(enemy);
        int steps = NativeTurnDriver.AdvanceUntil(session, () => enemy.Boosts[0] == 0, ExpirySteps);
        NativeTurnDriver.Report("lowerStat", 0x58, before, after, action, $"expired-after-steps={steps}");
    }

    /// <summary>The speed item lowers enemy speed by 64/128, then its modifier expires through the scheduler.</summary>
    private static void LowerSpeed()
    {
        using var session = new NativeMenuSession(includeOffensiveItems: true, fixture: new(setup => SetEnemyBase(setup, 1, 999, 100)));
        BattleFighter enemy = session.Enemy;
        string before = Boosts(enemy);
        string action = NativeTurnDriver.UseItemObserved(session, 0x59, "[0]");
        ProbeRequirement.Require(enemy.Boosts[2] == -50, $"lower speed vector -50 failed {Boosts(enemy)}");
        string after = Boosts(enemy);
        int steps = NativeTurnDriver.AdvanceUntil(session, () => enemy.Boosts[2] == 0, ExpirySteps);
        NativeTurnDriver.Report("lowerSpeed", 0x59, before, after, action, $"expired-after-steps={steps}");
    }

    /// <summary>Sets SYNTHETIC enemy base stats.</summary>
    /// <param name="setup">Fabricated setup before fighter initialization.</param>
    /// <param name="attack">Base attack.</param>
    /// <param name="defense">Base defense.</param>
    /// <param name="speed">Base speed.</param>
    private static void SetEnemyBase(BattleSetup setup, int attack, int defense, int speed)
    {
        BattleStats stats = NativeMenuFixture.Enemy(setup).BaseStats;
        stats.Stats[BattleStat.Attack] = attack;
        stats.Stats[BattleStat.Defense] = defense;
        stats.Stats[BattleStat.Speed] = speed;
    }

    /// <summary>Returns whether a fighter carries the confusion flag.</summary>
    /// <param name="fighter">Observed fighter.</param>
    /// <returns>Whether it is confused.</returns>
    private static bool Confused(BattleFighter fighter) => (fighter.Flags & FighterFlags.Confused) != 0;

    /// <summary>Formats attack, defense and speed modifiers.</summary>
    /// <param name="fighter">Observed fighter.</param>
    /// <returns>Proof fragment.</returns>
    private static string Boosts(BattleFighter fighter) => $"boosts={fighter.Boosts[0]}/{fighter.Boosts[1]}/{fighter.Boosts[2]}";
}
