namespace Dw3Production.Presentation;

/// <summary>Performs the entrance timeline's scene operations on the battle renderer.</summary>
internal sealed class BattleRendererEntranceStage : IBattleEntranceStage
{
    private readonly BattleRenderer _renderer;
    private readonly bool _enemy;
    private readonly Action _rebuildModels;
    private readonly Func<int, string> _stagePath;
    private readonly Action<string, string> _warn;
    private bool _flashWarned;

    /// <summary>Initializes a new instance of the <see cref="BattleRendererEntranceStage"/> class.</summary>
    /// <param name="renderer">Battle renderer owning camera, fighters and the sprite surface.</param>
    /// <param name="enemy">Whether the enemy side's fighter enters.</param>
    /// <param name="rebuildModels">Builds the new active pair and records it as rendered.</param>
    /// <param name="stagePath">Resolves a fight stage id to its prepared model document.</param>
    /// <param name="warn">Deduplicated host warning sink.</param>
    public BattleRendererEntranceStage(BattleRenderer renderer, bool enemy, Action rebuildModels, Func<int, string> stagePath, Action<string, string> warn)
    {
        _renderer = renderer;
        _enemy = enemy;
        _rebuildModels = rebuildModels;
        _stagePath = stagePath;
        _warn = warn;
    }

    /// <inheritdoc/>
    public void MoveCameraToFighter(bool enemy, int viewIndex, int frames) => _renderer.ActorScene.MoveCameraToFighterView(enemy, viewIndex, frames);

    /// <inheritdoc/>
    public void MoveCameraToEnemyView(int frames) => _renderer.ActorScene.MoveCameraToEnemyView(frames);

    /// <inheritdoc/>
    public void BeginEntranceCue() => _renderer.BeginEntranceAudio(immediate: true);

    /// <inheritdoc/>
    public void SetOpponentVisible(bool visible) => _renderer.ActorScene.SetOpponentVisible(_enemy, visible);

    /// <inheritdoc/>
    public void SwapInModel(int motion)
    {
        int opponentIdle = _renderer.ActorScene.FighterModel(!_enemy)?.Motion?.IdleMotion ?? 0;
        _rebuildModels();
        if (opponentIdle != 0 && _renderer.ActorScene.FighterModel(!_enemy) is { Motion: { } opponentMotion } opponent)
        {
            opponentMotion.SetIdleMotion(opponentIdle);
            opponent.SetMotion(opponentIdle + 1, restart: true);
        }

        _renderer.ActorScene.SetOpponentVisible(_enemy, visible: false);
        if (_renderer.ActorScene.FighterModel(_enemy) is not { } entering || entering.Motions.Count < motion)
        {
            _warn("battle-entrance-motion-unavailable", $"enemy={_enemy} motion={motion}");
            return;
        }

        entering.SetMotion(motion, restart: true);
    }

    /// <inheritdoc/>
    public void SetEnteringIdle(int idleMotion)
    {
        if (_renderer.ActorScene.FighterModel(_enemy) is { Motion: { } motion } entering)
        {
            motion.SetIdleMotion(idleMotion);
            entering.SetMotion(idleMotion + 1, restart: false);
        }
    }

    /// <inheritdoc/>
    public void PrepareEntranceExtras(BattleEntranceExtras extras)
    {
        ArgumentNullException.ThrowIfNull(extras);
        if (extras.FadedMusicId != 0)
        {
            _renderer.FadeOutBattleMusic(extras.FadedMusicId);
        }
    }

    /// <inheritdoc/>
    public void ApplyEntranceExtras(BattleEntranceExtras extras)
    {
        ArgumentNullException.ThrowIfNull(extras);
        if (extras.ChangesStage && !_renderer.ChangeStage(_stagePath(extras.StageId), extras.StageId))
        {
            _warn("battle-entrance-stage-unavailable", $"stage={extras.StageId}");
        }

        if (extras.ChangesMusic)
        {
            _renderer.BeginBattleMusic(extras.MusicId);
        }
    }

    /// <inheritdoc/>
    public void PresentFlash(int level)
    {
        if (!_renderer.ActorScene.PresentScreenFlash(level) && level > 0 && !_flashWarned)
        {
            _flashWarned = true;
            _warn("battle-entrance-flash-unavailable", $"enemy={_enemy}");
        }
    }
}
