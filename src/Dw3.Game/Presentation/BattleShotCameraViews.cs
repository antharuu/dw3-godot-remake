namespace Dw3Production.Presentation;

/// <summary>Provides fresh overview and fighter camera frames for battle shots.</summary>
public sealed class BattleShotCameraViews
{
    /// <summary>Gets a fresh battle overview frame.</summary>
    public required Func<SourceBattleCameraView> GetEnemyView { get; init; }

    /// <summary>Gets a fresh frame for a fighter slot and camera index.</summary>
    public required Func<int, int, SourceBattleCameraView> GetFighterView { get; init; }
}
