namespace Dw3Production.Diagnostics.Battle;

/// <summary>One completed fighter-sheet catalog row, including static and production-render outcomes.</summary>
public sealed record FighterSheetResult(
    int FighterId,
    bool Enemy,
    int CameraCount,
    IReadOnlyList<FighterSheetFailure> Failures,
    string? Framing,
    string? Detail);
