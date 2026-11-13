using Dw3Production.Diagnostics.Field;
using Dw3Production.Simulation;
using Godot;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Native host matrix for the remaining partner and field item families. Every precondition is SYNTHETIC and
/// mirrors a qualified B9-046a/b vector, while menu selection, the item effect, scheduling and expiry run production code.</summary>
internal static class NativePartnerItemMatrixProof
{
    /// <summary>Maximum ordinary steps allowed before a scheduled expiry must have been consumed.</summary>
    private const int ExpirySteps = 64;

    /// <summary>Runs every partner and field family case.</summary>
    public static void Run()
    {
        BoostAttack();
        BoostDefense();
        Special();
        FillGauge();
        BoostRandomElement();
        GD.Print("NATIVE_ROOT_MENU_PROOF PASS item-matrix=boostAttack-boostDefense-special-fillGauge-boostRandomElement fixture=SYNTHETIC ingress=GameHost._Input");
    }

    /// <summary>Attack boost raises attack, trades defense, then both modifiers expire through the scheduler.</summary>
    private static void BoostAttack() => Tradeoff("boostAttack", 0x49, raised: 0, lowered: 1);

    /// <summary>Defense boost raises defense, trades attack, then both modifiers expire through the scheduler.</summary>
    private static void BoostDefense() => Tradeoff("boostDefense", 0x4A, raised: 1, lowered: 0);

    /// <summary>Runs one source trade-off vector with attack and defense bases of 50.</summary>
    /// <param name="family">Family label for the proof line.</param>
    /// <param name="itemId">Source-shaped item id.</param>
    /// <param name="raised">Boost index raised by 128/128.</param>
    /// <param name="lowered">Boost index lowered by a quarter of the amount.</param>
    private static void Tradeoff(string family, int itemId, int raised, int lowered)
    {
        using var session = new NativeMenuSession(includePartnerItems: true, fixture: new(setup => SetCombatBase(setup, 50)));
        BattleFighter player = session.Player;
        string before = $"boosts={player.Boosts[0]}/{player.Boosts[1]}";
        string action = NativeTurnDriver.UseItemObserved(session, itemId, "[0,0]");
        ProbeRequirement.Require(player.Boosts[raised] == 50 && player.Boosts[lowered] == -12, $"{family} vector 50/-12 not applied boosts={player.Boosts[0]}/{player.Boosts[1]}");
        string after = $"boosts={player.Boosts[0]}/{player.Boosts[1]}";
        int steps = NativeTurnDriver.AdvanceUntil(session, () => player.Boosts[0] == 0 && player.Boosts[1] == 0, ExpirySteps);
        NativeTurnDriver.Report(family, itemId, before, after, action, $"expired-after-steps={steps}");
    }

    /// <summary>Special status is set by the item and cleared by its scheduled SpecialEnd consumer.</summary>
    private static void Special()
    {
        using var session = new NativeMenuSession(includePartnerItems: true);
        BattleFighter player = session.Player;
        string before = $"special={player.Special}";
        string action = NativeTurnDriver.UseItemObserved(session, 0x4B, "[0]");
        ProbeRequirement.Require(player.Special, "special status not applied");
        int steps = NativeTurnDriver.AdvanceUntil(session, () => !player.Special, ExpirySteps);
        NativeTurnDriver.Report("special", 0x4B, before, "special=True", action, $"expired-after-steps={steps}");
    }

    /// <summary>Gauge fill caps at 999 from a SYNTHETIC gauge of 980 without random draws or a Blast trigger.</summary>
    private static void FillGauge()
    {
        using var session = new NativeMenuSession(includePartnerItems: true, fixture: new(setup => NativeMenuFixture.Partner(setup).BlastGauge = 980));
        BattleFighter player = session.Player;
        string before = $"gauge={player.BlastGauge}";
        string action = NativeTurnDriver.UseItemObserved(session, 0x4C, "[]");
        ProbeRequirement.Require(player.BlastGauge == 999, $"gauge fill vector 980->999 failed gauge={player.BlastGauge} {action}");
        NativeTurnDriver.ToPlayerCommand(session);
        ProbeRequirement.Require(player.BlastGauge == 999 && !player.Temporary, "gauge fill crossed the Blast threshold");
        NativeTurnDriver.Report("fillGauge", 0x4C, before, $"gauge={player.BlastGauge}", action, "immediate=no-expiry");
    }

    /// <summary>Random element draws its range and magnitude from the host stream, then its field boost expires.</summary>
    private static void BoostRandomElement()
    {
        using var session = new NativeMenuSession(includeFieldItems: true, fixture: new(RngScript: [2000, 2000]));
        string before = $"field={session.Flow.State.Boost}";
        string action = NativeTurnDriver.UseItemObserved(session, 0x54, "[2000,2000]");
        ProbeRequirement.Require(session.Flow.State.Boost == new ElementBoost(7, 127), $"random element vector (7,127) failed field={session.Flow.State.Boost} {action}");
        string after = $"field={session.Flow.State.Boost}";
        int steps = NativeTurnDriver.AdvanceUntil(session, () => session.Flow.State.Boost == ElementBoost.None, ExpirySteps);
        NativeTurnDriver.Report("boostRandomElement", 0x54, before, after, action, $"expired-after-steps={steps}");
    }

    /// <summary>Sets the active partner's SYNTHETIC attack and defense bases.</summary>
    /// <param name="setup">Fabricated setup before fighter initialization.</param>
    /// <param name="value">Shared base value.</param>
    private static void SetCombatBase(BattleSetup setup, int value)
    {
        NativeMenuFixture.Partner(setup).Stats.Stats[BattleStat.Attack] = value;
        NativeMenuFixture.Partner(setup).Stats.Stats[BattleStat.Defense] = value;
    }
}
