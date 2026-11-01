namespace Dw3Production.Presentation;

/// <summary>
/// The command-menu shot camera: while the player picks a command, the full battle scene cycles
/// through the fighters' source camera views. It starts on the enemy view, which is the player
/// fighter's camera at index <c>enemyCameraCount - 1</c>. Cameras 6 and 7 belong only to the
/// small partner view and are never used for the full viewport.
/// </summary>
internal sealed class BattleCommandCamera
{
    private readonly IShotCameraRng _rng = new RuntimeShotRng();
    private BattleShotCamera? _shot;

    /// <summary>Gets the running shot camera, or null outside the command menu.</summary>
    public BattleShotCamera? Shot => _shot;

    /// <summary>Starts a new shot sequence over the two fighters' cameras.</summary>
    /// <param name="players">Camera views of the player fighter (slot 0).</param>
    /// <param name="enemies">Camera views of the enemy fighter (slot 1).</param>
    /// <returns>The first view to show.</returns>
    /// <exception cref="InvalidOperationException">Either fighter has no camera.</exception>
    public SourceBattleCameraView Start(IReadOnlyList<BattleModel.BattleCameraView> players, IReadOnlyList<BattleModel.BattleCameraView> enemies)
    {
        if (players.Count == 0 || enemies.Count == 0)
        {
            throw new InvalidOperationException("source shot camera unavailable");
        }

        _shot = new BattleShotCamera(_rng, new BattleShotCameraViews
        {
            GetEnemyView = () => ViewFor(players, enemies, enemy: false, enemies.Count - 1),
            GetFighterView = (slot, index) => ViewFor(players, enemies, enemy: slot != 0, index),
        });
        return _shot.View;
    }

    /// <summary>Advances the running shot sequence by one frame.</summary>
    /// <returns>The view to show, or null when no sequence runs.</returns>
    public SourceBattleCameraView? Step()
    {
        if (_shot is null)
        {
            return null;
        }

        _shot.Step();
        return _shot.View;
    }

    /// <summary>Stops the running sequence, if any.</summary>
    public void Stop()
    {
        _shot?.Stop();
        _shot = null;
    }

    /// <summary>Describes the sequence state for a probe log line.</summary>
    /// <returns>"inactive", or the list, shot, substate, Y rotation, time and fighter visibility.</returns>
    public string Summary() => _shot is null ? "inactive" :
        $"list={_shot.List} shot={_shot.Shot} substate={_shot.Substate} rot.vy=0x{_shot.View.RotationY:X3} time={_shot.Time} player={_shot.PlayerVisible} enemy={_shot.EnemyVisible}";

    private static SourceBattleCameraView ViewFor(IReadOnlyList<BattleModel.BattleCameraView> players, IReadOnlyList<BattleModel.BattleCameraView> enemies, bool enemy, int index)
    {
        var cameras = enemy ? enemies : players;
        if (index < 0 || index >= cameras.Count)
        {
            throw new InvalidOperationException($"source camera index={index} unavailable enemy={enemy}");
        }

        var source = cameras[index];
        return new SourceBattleCameraView
        {
            Vpx = source.CamPos[0],
            Vpy = source.CamPos[1],
            Vpz = source.CamPos[2],
            Vrx = source.CamRef[0],
            Vry = source.CamRef[1],
            Vrz = source.CamRef[2],
            Projection = source.Proj,
        };
    }
}
