namespace Dw3Production.Simulation;

/// <summary>
/// Resolves a technique that rolls no hit. The action's previous hit record is cleared first, as the source clears
/// its action state before a non-damaging technique, so the result message never shows an earlier action's hits.
/// A healing technique records its frozen result and spends the user's charge. Any other no-hit effect keeps its
/// existing effect consumer.
/// </summary>
internal static class BattleNoHitTechnique
{
    /// <summary>Applies one no-hit technique for a side.</summary>
    /// <param name="state">Battle receiving the technique.</param>
    /// <param name="side">Side of the user.</param>
    /// <param name="technique">Technique that requires no hit sequence.</param>
    /// <param name="rng">Shared battle random source.</param>
    public static void Resolve(BattleState state, BattleSide side, TechData technique, IBattleRng rng)
    {
        state.BeginNoHitAction();
        if (BattleTechniqueRecovery.Supports(technique))
        {
            state.LastRecovery = BattleTechniqueRecovery.Apply(state, side, technique);
            state.ConsumeCharge(side);
            return;
        }

        state.ApplyTechniqueEffect(side, technique, rng, 0);
    }
}
