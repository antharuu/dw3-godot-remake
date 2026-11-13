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

    /// <summary>Gets the frozen result of a healing technique, or null for an action that heals nothing.</summary>
    public BattleRecoveryOutcome? Recovery { get; init; }

    /// <summary>Gets frozen sound-substitution results. Only marker 3 selects the miss sound.</summary>
    public IReadOnlyList<int> SoundResults { get; init; } = [0, 0, 0, 0];

    /// <summary>Builds a context when the technique has a valid script selector, even without damage events;
    /// returns null otherwise. Callers assign the result to <c>PendingSequenceContext</c> without branching.</summary>
    /// <param name="side">Side that initiated the action.</param>
    /// <param name="tech">Technique; must have a positive Script selector to qualify.</param>
    /// <param name="attackerId">Attacker fighter id, frozen before any KO swap.</param>
    /// <param name="targetId">Target fighter id, frozen before any KO swap.</param>
    /// <param name="hitEvents">Pre-resolved hit events from the single RNG draw.</param>
    /// <param name="recovery">Frozen result of a healing technique, or null.</param>
    /// <returns>A new context, or null when no script should run.</returns>
    public static ActionSequenceContext? TryCapture(
        BattleSide side,
        TechData? tech,
        int attackerId,
        int targetId,
        IReadOnlyList<BattleHitEvent> hitEvents,
        BattleRecoveryOutcome? recovery = null)
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
            SoundResults = CaptureSoundResults(hitEvents, tech.Effect),
            Recovery = recovery,
        };
    }

    /// <summary>Freezes hit and terminal miss markers without re-reading HP or drawing randomness.</summary>
    /// <param name="hits">Already resolved ordered hits, excluding counter reactions.</param>
    /// <param name="effect">Frozen technique effect, identifying the two-slot magic layout.</param>
    /// <returns>Three intermediate markers and one terminal marker.</returns>
    private static int[] CaptureSoundResults(IReadOnlyList<BattleHitEvent> hits, int effect)
    {
        var ordered = hits.Where(hit => !hit.IsCounter).ToArray();
        int[] results = [0, 0, 0, 0];
        for (int index = 0; index < Math.Min(3, ordered.Length - 1); index++)
        {
            results[index] = ordered[index].Landed ? 0 : 3;
        }

        if (ordered.Length > 0)
        {
            var last = ordered[^1];
            results[3] = last.TargetDefeated ? 2 : TerminalSoundMarker(last, effect);
            if (last.TargetDefeated && effect == BattleTechniqueEffect.DoubleMagic)
            {
                results[0] = ordered.Count(hit => hit.Landed) == 1 ? 3 : 0;
            }
        }

        return results;
    }

    /// <summary>Distinguishes an ordinary terminal hit from the second magic slot without re-resolving rules.</summary>
    /// <param name="hit">Frozen last result.</param>
    /// <param name="effect">Frozen technique effect.</param>
    /// <returns>Miss, intermediate magic hit, or ordinary terminal hit marker.</returns>
    private static int TerminalSoundMarker(BattleHitEvent hit, int effect)
    {
        if (!hit.Landed)
        {
            return 3;
        }

        return effect == BattleTechniqueEffect.DoubleMagic ? 0 : 1;
    }
}
