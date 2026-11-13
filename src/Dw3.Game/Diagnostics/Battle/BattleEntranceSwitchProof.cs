using Dw3Production.Presentation;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>
/// Plays an accepted partner switch's entrance call through the production frame coordinator: the outgoing partner
/// is called back (MFIGHT 0x38) with the scene untouched, the source entrance plays, then the incoming partner is
/// named (0x39) while the camera returns, before the battle goes on. See <see cref="BattleEntranceCallProof"/>.
/// </summary>
internal static class BattleEntranceSwitchProof
{
    /// <summary>Time units the post message stays open, beyond the player's 60-unit camera return.</summary>
    private const int ConfirmAfter = 75;

    /// <summary>Plays the switch's entrance call and asserts its observable stages.</summary>
    /// <param name="host">Live battle probe host.</param>
    /// <param name="captureRoot">Private capture directory.</param>
    /// <param name="outgoing">Fighter id rendered before the switch.</param>
    /// <param name="incoming">Fighter id chosen by the switch.</param>
    /// <returns>The number of source frames of the entrance itself.</returns>
    public static async Task<int> RunAsync(IBattleProbeHost host, string captureRoot, int outgoing, int incoming)
    {
        var fighter = host.Flow.State.ActivePlayerFighter ?? throw new InvalidOperationException("switch left no active partner");
        int idle = BattleWeakIdleRule.Select(fighter.Hp, 0, fighter.MaxHp);
        var driver = new BattleProductionFrameDriver(host.FrameHost);
        var expected = new BattleEntranceExpectation("switch", BattleEntranceKind.PlayerSwitch, outgoing, incoming, idle, ConfirmAfter);
        var samples = await new BattleEntranceCallProof(host, driver, captureRoot).RunAsync(expected);
        return samples.Max(sample => sample.Frame);
    }
}
