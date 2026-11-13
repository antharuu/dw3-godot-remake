using Dw3Production.Simulation;

namespace Dw3Production.Presentation;

/// <summary>
/// What the battle view shows while an entrance's caller runs. The source keeps the outgoing fighter active until the
/// incoming model is in place, so its status stays on the HUD until then. No command menu exists meanwhile, the flow's
/// own message is replaced by the caller's message box, and the box is hidden while the entrance itself plays.
/// </summary>
internal static class BattleEntranceView
{
    /// <summary>Applies the entrance's view rules to a built battle view.</summary>
    /// <param name="view">View built from the already mutated flow.</param>
    /// <param name="call">Running entrance call.</param>
    /// <param name="outgoingSlot">Slot rendered before the entrance on the entering side.</param>
    /// <returns>The view to present.</returns>
    public static BattleView Apply(BattleView view, BattleEntranceCall call, int outgoingSlot)
    {
        ArgumentNullException.ThrowIfNull(call);
        var held = WithMessage(view with { Menu = null, Phase = WithoutCommand(view.Phase) }, call);
        if (call.Sequence.Swapped)
        {
            return held;
        }

        return call.Sequence.Enemy
            ? held with { Enemies = HoldActive(view.Enemies, outgoingSlot) }
            : held with { Players = HoldActive(view.Players, outgoingSlot) };
    }

    /// <summary>Presents a command phase as a sequence: no command menu, partner view or command camera exists while
    /// the caller runs, the battle tick holding the flow until it ends.</summary>
    /// <param name="phase">Flow phase.</param>
    /// <returns>The phase to present.</returns>
    private static BattleFlowPhase WithoutCommand(BattleFlowPhase phase) =>
        phase == BattleFlowPhase.PlayerCommand ? BattleFlowPhase.ActionSequence : phase;

    /// <summary>Shows the caller's open message box, or no box while the entrance plays.</summary>
    /// <param name="view">View to update.</param>
    /// <param name="call">Running entrance call.</param>
    /// <returns>The view with the caller's message state.</returns>
    private static BattleView WithMessage(BattleView view, BattleEntranceCall call)
    {
        if (call.OpenMessage is not { } open)
        {
            return view with { MessageVisible = false };
        }

        return view with
        {
            Message = open.Message.Text.Debug,
            MessageText = open.Message.Text,
            MessageVisible = true,
            MessageLines = open.Reveal.VisibleLines,
            MessageArrow = open.Reveal.Arrow,
        };
    }

    /// <summary>Marks the outgoing slot as the active one. Slots, not fighter ids, identify the fighter, since two
    /// fighters of the same kind may switch.</summary>
    /// <param name="fighters">Side fighter slots as built from the flow.</param>
    /// <param name="outgoingSlot">Outgoing slot.</param>
    /// <returns>The side with only the outgoing slot active, or unchanged when the slot is absent.</returns>
    private static IReadOnlyList<BattleFighterView> HoldActive(IReadOnlyList<BattleFighterView> fighters, int outgoingSlot) =>
        outgoingSlot < 0 || outgoingSlot >= fighters.Count
            ? fighters
            : [.. fighters.Select((fighter, i) => fighter with { Active = i == outgoingSlot })];
}
