namespace Dw3Production.Simulation;

/// <summary>Calculates the qualified battle-event delay families from simulation inputs.</summary>
public static class BattleActionDelay
{
    /// <summary>Calculates the delay for one event family.</summary>
    /// <param name="kind">Family selected by the action or status producer.</param>
    /// <param name="ownSpeed">Speed of the side receiving the event.</param>
    /// <param name="otherSpeed">Speed of its current opponent.</param>
    /// <param name="spirit">Spirit of the side receiving the event.</param>
    /// <param name="rng">Shared injected battle random source.</param>
    /// <returns>The non-negative delay in battle-time units.</returns>
    public static int Calculate(BattleActionDelayKind kind, int ownSpeed, int otherSpeed, int spirit, IBattleRng rng) => kind switch
    {
        BattleActionDelayKind.NormalTurn => DamageCalculator.GetTurnDelay(ownSpeed, otherSpeed),
        BattleActionDelayKind.RunAway => RunAwayDelay(ownSpeed, otherSpeed),
        BattleActionDelayKind.AutoRecovery => RandomSpiritDelay(spirit, rng, 0, 1001),
        BattleActionDelayKind.BlastTwoSeconds => 2000,
        BattleActionDelayKind.BlastTwoAndHalfSeconds => 2500,
        BattleActionDelayKind.BlastThreeSeconds => 3000,
        BattleActionDelayKind.BlastThreeAndHalfSeconds => 3500,
        BattleActionDelayKind.BlastFourSeconds => 4000,
        BattleActionDelayKind.RandomStatusExpiry => Math.Clamp(rng.NextValue() % 8001, 2000, 6000),
        BattleActionDelayKind.BoostExpiry => RandomSpiritDelay(spirit, rng, 2000, 0),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Status-specific delay inputs are required."),
    };

    /// <summary>Calculates the speed-scaled run-away family from the normal delay ratio.</summary>
    /// <param name="ownSpeed">Speed of the side receiving the next event.</param>
    /// <param name="otherSpeed">Speed of the opposing active fighter.</param>
    /// <returns>A clamped 176..353 delay.</returns>
    private static int RunAwayDelay(int ownSpeed, int otherSpeed) =>
        Math.Clamp(DamageCalculator.GetTurnDelay(ownSpeed, otherSpeed) / 4, 176, 353);

    /// <summary>Calculates a random spirit-based family.</summary>
    /// <param name="spirit">Spirit of the affected fighter.</param>
    /// <param name="rng">Shared injected battle random source.</param>
    /// <param name="baseDelay">Fixed delay added before the random component.</param>
    /// <param name="minimum">Inclusive floor, or zero when there is no floor.</param>
    /// <returns>The family delay.</returns>
    private static int RandomSpiritDelay(int spirit, IBattleRng rng, int baseDelay, int minimum)
    {
        int delay = baseDelay + (Math.Max(spirit, 0) * 10) + (rng.NextValue() % 2001);
        return minimum == 0 ? delay : Math.Max(delay, minimum);
    }
}
