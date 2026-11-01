namespace Dw3Production.Simulation;

/// <summary>Describes the visible state of one party member's report panel.</summary>
public sealed record FightReportPartnerView(
    int PartyIndex,
    int PartnerId,
    int FighterId,
    int Fade,
    bool Selected,
    int CursorFrame,
    int LevelBlinkFrame,
    int ShownExp,
    int AnimationFrame,
    IReadOnlyList<FightReportSlotView> Slots);
