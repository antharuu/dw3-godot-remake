namespace Dw3Production.Diagnostics.Battle;

/// <summary>One model discovered directly from the decoded pack and its static preflight verdict.</summary>
public sealed record FighterSheetCatalogEntry(
    int FighterId,
    string Path,
    bool IsEnemy,
    int CameraCount,
    IReadOnlyList<FighterSheetFailure> Failures)
{
    /// <summary>Gets a value indicating whether static preflight permits a production render attempt.</summary>
    public bool CanRender => Failures.Count == 0;
}
