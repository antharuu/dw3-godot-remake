namespace Dw3Production.Simulation;

/// <summary>Frozen identity and pre-resolved hit context captured before a damaging action starts its timeline.
/// All fields are sealed at the moment the action commits; they are never re-read from live state during playback.</summary>
/// <param name="AttackerSide">Side that initiated the action.</param>
/// <param name="AttackerId">Fighter id of the attacker, frozen before any KO swap.</param>
/// <param name="TargetId">Fighter id of the target, frozen before any KO swap.</param>
/// <param name="HitEvents">Pre-resolved hit events from the single RNG draw; must not be re-rolled for animation.</param>
/// <param name="ScriptSelector">Technique script selector for this action; 0 means no script available.</param>
public sealed record ActionSequenceContext(
    BattleSide AttackerSide,
    int AttackerId,
    int TargetId,
    IReadOnlyList<BattleHitEvent> HitEvents,
    int ScriptSelector)
{
    /// <summary>Gets the resolved optional action stage, minus one for no stage change.</summary>
    public int ScriptStage { get; init; } = -1;

    /// <summary>Gets the frozen technique sprite selector used by the dynamic effect marker.</summary>
    public int ScriptEffect { get; init; }

    /// <summary>Gets the technique's frozen action-sound index.</summary>
    public int ScriptSound { get; init; }

    /// <summary>Gets frozen sound-substitution results. Only marker 3 selects the miss sound.</summary>
    public IReadOnlyList<int> SoundResults { get; init; } = [0, 0, 0, 0];

    /// <summary>Builds a context when the technique has a valid script selector, even without damage events;
    /// returns null otherwise. Callers assign the result to <c>PendingSequenceContext</c> without branching.</summary>
    /// <param name="side">Side that initiated the action.</param>
    /// <param name="tech">Technique; must have a positive Script selector to qualify.</param>
    /// <param name="attackerId">Attacker fighter id, frozen before any KO swap.</param>
    /// <param name="targetId">Target fighter id, frozen before any KO swap.</param>
    /// <param name="hitEvents">Pre-resolved hit events from the single RNG draw.</param>
    /// <returns>A new context, or null when no script should run.</returns>
    public static ActionSequenceContext? TryCapture(
        BattleSide side,
        TechData? tech,
        int attackerId,
        int targetId,
        IReadOnlyList<BattleHitEvent> hitEvents)
    {
        if (tech is not { Script: > 0 })
        {
            return null;
        }

        return new ActionSequenceContext(side, attackerId, targetId, [.. hitEvents], tech.Script)
        {
            ScriptStage = tech.ScriptStage == 0 ? -1 : tech.ScriptStage,
            ScriptEffect = tech.ScriptEffect,
            ScriptSound = tech.ScriptSound,
            SoundResults = CaptureSoundResults(hitEvents),
        };
    }

    /// <summary>Freezes hit and terminal miss markers without re-reading HP or drawing randomness.</summary>
    /// <param name="hits">Already resolved ordered hits, excluding counter reactions.</param>
    /// <returns>Three intermediate markers and one terminal marker.</returns>
    private static int[] CaptureSoundResults(IReadOnlyList<BattleHitEvent> hits)
    {
        var ordered = hits.Where(hit => !hit.IsCounter).ToArray();
        int[] results = [0, 0, 0, 0];
        for (int index = 0; index < Math.Min(3, ordered.Length - 1); index++)
        {
            results[index] = ordered[index].Landed ? 0 : 3;
        }

        if (ordered.Length > 0)
        {
            results[3] = ordered[^1].Landed ? 1 : 3;
        }

        return results;
    }
}
