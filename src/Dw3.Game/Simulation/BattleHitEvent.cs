namespace Dw3Production.Simulation;

/// <summary>One ordered damage-resolution event from the most recent battle action.</summary>
/// <param name="AttackerSide">Side that rolled the hit.</param>
/// <param name="HitIndex">Zero-based index in the action's ordered hit sequence.</param>
/// <param name="Landed">Whether the hit landed.</param>
/// <param name="Damage">Damage actually removed from the target's HP, zero for a miss.</param>
/// <param name="Effect">Technique effect associated with this ordered hit.</param>
/// <param name="IsCounter">Whether the hit came from the one allowed counter-reaction.</param>
public sealed record BattleHitEvent(BattleSide AttackerSide, int HitIndex, bool Landed, int Damage, int Effect, bool IsCounter)
{
    /// <summary>Gets a value indicating whether this hit depleted the original target's HP.</summary>
    public bool TargetDefeated { get; init; }

    /// <summary>Gets the landed hit's damage as the formula returned it, before the target's remaining HP clamps
    /// the removal. The source's action messages print this value. Zero for a miss.</summary>
    public int FormulaDamage { get; init; }
}
