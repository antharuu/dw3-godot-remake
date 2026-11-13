namespace Dw3Production.Simulation;

/// <summary>Describes the visible state of one party member's report panel.</summary>
/// <param name="PartyIndex">Party slot.</param>
/// <param name="PartnerId">Partner number among the eight partners, selecting the portrait animation.</param>
/// <param name="FighterId">Fighter id whose decoded name is shown.</param>
/// <param name="Fade">Panel fade level, 4096 when fully shown.</param>
/// <param name="Selected">Whether the selection cursor is shown.</param>
/// <param name="CursorFrame">Selection cursor colour row.</param>
/// <param name="LevelBlinkFrame">Partner level-up blink colour row.</param>
/// <param name="ShownExp">Rolled experience shown.</param>
/// <param name="Level">Partner level shown.</param>
/// <param name="AnimationFrame">Portrait animation steps elapsed.</param>
/// <param name="Slots">The three form slot rows.</param>
public sealed record FightReportPartnerView(
    int PartyIndex,
    int PartnerId,
    int FighterId,
    int Fade,
    bool Selected,
    int CursorFrame,
    int LevelBlinkFrame,
    int ShownExp,
    int Level,
    int AnimationFrame,
    IReadOnlyList<FightReportSlotView> Slots)
{
    /// <summary>Gets a value indicating whether the panel runs its raise, when the original draws its blink markers.</summary>
    public bool Raising { get; init; }

    /// <summary>Gets the slot row the raise is on, or -1 when no occupied row is current.</summary>
    public int SlotCursor { get; init; } = -1;

    /// <summary>Gets a value indicating whether the partner level-up blink is on.</summary>
    public bool LevelBlinking { get; init; }

    /// <summary>Gets a value indicating whether the panel's text windows are shown: after its fade in, until its fade out starts.</summary>
    public bool TextVisible { get; init; }
}
