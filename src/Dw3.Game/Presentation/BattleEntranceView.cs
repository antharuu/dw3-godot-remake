namespace Dw3Production.Presentation;

/// <summary>
/// What the battle view shows while an entrance plays. The source keeps the outgoing fighter active until the incoming
/// model is in place, so its status stays on the HUD until then, and the flow's message waits for the entrance's end.
/// </summary>
internal static class BattleEntranceView
{
    /// <summary>Applies the entrance's view rules to a built battle view.</summary>
    /// <param name="view">View built from the already mutated flow.</param>
    /// <param name="entrance">Playing entrance.</param>
    /// <param name="outgoingId">Fighter id rendered before the entrance on the entering side.</param>
    /// <returns>The view to present.</returns>
    public static BattleView Apply(BattleView view, BattleEntranceSequence entrance, int outgoingId)
    {
        var held = view with { MessageVisible = false };
        if (entrance.Swapped)
        {
            return held;
        }

        return entrance.Enemy
            ? held with { Enemies = HoldActive(view.Enemies, outgoingId) }
            : held with { Players = HoldActive(view.Players, outgoingId) };
    }

    /// <summary>Marks the first fighter of the outgoing id as the active one.</summary>
    /// <param name="fighters">Side fighters as built from the flow.</param>
    /// <param name="outgoingId">Outgoing fighter id.</param>
    /// <returns>The side with only the outgoing fighter active, or unchanged when it is absent.</returns>
    private static IReadOnlyList<BattleFighterView> HoldActive(IReadOnlyList<BattleFighterView> fighters, int outgoingId)
    {
        int index = fighters.ToList().FindIndex(fighter => fighter.Id == outgoingId);
        return index < 0 ? fighters : [.. fighters.Select((fighter, i) => fighter with { Active = i == index })];
    }
}
