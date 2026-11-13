namespace Dw3Production.Presentation;

/// <summary>
/// The battle scene operations of actor transitions: source fighter and enemy views through the shared camera
/// interpolation, the opponent's visibility and the additive white level of the sprite surface. While a transition is
/// active, the command shot camera neither moves the camera nor restores the hidden opponent.
/// </summary>
internal sealed class BattleActorTransitionScene
{
    private readonly BattleFighterRoster _fighters;
    private readonly BattleCameraFade _camera;
    private readonly Action _applyCamera;
    private readonly Func<bool, IReadOnlyList<BattleModel.BattleCameraView>> _cameras;
    private readonly BattleRendererSequencePlayback _playback;

    /// <summary>Initializes a new instance of the <see cref="BattleActorTransitionScene"/> class.</summary>
    /// <param name="fighters">Physical fighter roster.</param>
    /// <param name="camera">Shared physical camera interpolation.</param>
    /// <param name="applyCamera">Applies the interpolated camera to the viewport camera.</param>
    /// <param name="cameras">Decoded camera views of the player's (false) or enemy's (true) model.</param>
    /// <param name="playback">Sequence playback owning the sprite surface and the menu camera.</param>
    public BattleActorTransitionScene(
        BattleFighterRoster fighters,
        BattleCameraFade camera,
        Action applyCamera,
        Func<bool, IReadOnlyList<BattleModel.BattleCameraView>> cameras,
        BattleRendererSequencePlayback playback)
    {
        _fighters = fighters;
        _camera = camera;
        _applyCamera = applyCamera;
        _cameras = cameras;
        _playback = playback;
    }

    /// <summary>Gets or sets a value indicating whether an actor transition owns the camera and fighter visibility.</summary>
    public bool Active
    {
        get => _playback.ActorTransitionActive;
        set => _playback.ActorTransitionActive = value;
    }

    /// <summary>Moves the camera to a decoded fighter view through the source camera interpolation.</summary>
    /// <param name="enemy">Whether the view belongs to the enemy side's current model.</param>
    /// <param name="viewIndex">Decoded fighter camera index.</param>
    /// <param name="frames">Source fade frames, zero for an immediate cut.</param>
    public void MoveCameraToFighterView(bool enemy, int viewIndex, int frames)
    {
        _camera.BeginSource(BattleSequenceCamera.FighterView(_cameras(enemy), viewIndex), frames);
        _applyCamera();
    }

    /// <summary>Moves the camera to the source enemy view: the player's view at the enemy's camera count less one.</summary>
    /// <param name="frames">Source fade frames.</param>
    public void MoveCameraToEnemyView(int frames)
    {
        _camera.BeginSource(BattleSequenceCamera.FighterView(_cameras(false), _cameras(true).Count - 1), frames);
        _applyCamera();
    }

    /// <summary>Hides the side that is not entering, or shows every fighter again.</summary>
    /// <param name="enemyEntering">Whether the enemy side's fighter enters.</param>
    /// <param name="visible">Whether the opponent is drawn.</param>
    public void SetOpponentVisible(bool enemyEntering, bool visible)
    {
        if (visible)
        {
            _fighters.RestoreVisibility();
        }
        else
        {
            _fighters.ApplyShotVisibility(playerVisible: !enemyEntering, enemyVisible: enemyEntering);
        }
    }

    /// <summary>Returns the active model of one side.</summary>
    /// <param name="enemy">Whether to return the enemy side's model.</param>
    /// <returns>The built model, or <see langword="null"/>.</returns>
    public BattleModel? FighterModel(bool enemy) => enemy ? _fighters.Enemy : _fighters.Player;

    /// <summary>Draws a full-screen additive white level on the battle sprite surface, kept by its physical steps.</summary>
    /// <param name="level">Level from 0 to 255.</param>
    /// <returns>Whether the prepared sprite surface exists.</returns>
    public bool PresentScreenFlash(int level)
    {
        if (_playback.DiagnosticSprites is not { } sprites)
        {
            return false;
        }

        sprites.ExternalFlashLevel = level;
        sprites.Surface.Present(sprites.Active, sprites.FlashLevel);
        return true;
    }
}
