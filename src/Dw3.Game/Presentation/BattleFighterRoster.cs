using Godot;

namespace Dw3Production.Presentation;

/// <summary>
/// The decoded fighter models of the current battle, slot 0 the player and slot 1 the enemy,
/// with the per-fighter operations the presentation and its probes apply to them.
/// </summary>
internal sealed class BattleFighterRoster
{
    private readonly List<BattleModel> _models = [];
    private readonly HashSet<int> _probeHiddenFighters = [];

    /// <summary>Gets the built models in slot order.</summary>
    public IReadOnlyList<BattleModel> Models => _models;

    /// <summary>Gets the player model (slot 0), or null when none is built.</summary>
    public BattleModel? Player => _models.Count > 0 ? _models[0] : null;

    /// <summary>Gets the enemy model (slot 1), or null when none is built.</summary>
    public BattleModel? Enemy => _models.Count > 1 ? _models[1] : null;

    /// <summary>Appends a model to the next slot.</summary>
    /// <param name="model">A loaded model.</param>
    public void Add(BattleModel model) => _models.Add(model);

    /// <summary>Frees every model and forgets the probe visibility mask.</summary>
    public void Clear()
    {
        _probeHiddenFighters.Clear();
        foreach (var model in _models)
        {
            model.Root.QueueFree();
        }

        _models.Clear();
    }

    /// <summary>Finds a model by fighter id.</summary>
    /// <param name="fighterId">Fighter id.</param>
    /// <returns>The first matching model, or null.</returns>
    public BattleModel? Find(int fighterId) => _models.FirstOrDefault(model => model.FighterId == fighterId);

    /// <summary>Finds a model by fighter id, which must be built.</summary>
    /// <param name="fighterId">Fighter id.</param>
    /// <returns>The first matching model.</returns>
    /// <exception cref="InvalidOperationException">No such fighter is built.</exception>
    public BattleModel Require(int fighterId) =>
        Find(fighterId) ?? throw new InvalidOperationException($"battle model fighter={fighterId} is not built");

    /// <summary>Poses every model at one source frame.</summary>
    /// <param name="frame">Source animation frame.</param>
    public void PoseAll(int frame)
    {
        foreach (var model in _models)
        {
            model.Pose(frame);
        }
    }

    /// <summary>Poses every model at a keyframe of one motion; models without that motion keep their pose.</summary>
    /// <param name="motionIndex">Zero-based motion index.</param>
    /// <param name="frameInMotion">Keyframe position in the motion, clamped to its range.</param>
    public void PoseMotionAll(int motionIndex, int frameInMotion)
    {
        foreach (var model in _models)
        {
            var keyframes = model.MotionKeyframes(motionIndex);
            if (keyframes.Count > 0)
            {
                model.Pose(keyframes[Mathf.Clamp(frameInMotion, 0, keyframes.Count - 1)]);
            }
        }
    }

    /// <summary>Requests a one-based source motion on every model.</summary>
    /// <param name="motion">One-based motion number.</param>
    /// <param name="restart">True to restart the motion when it is already active.</param>
    public void SetMotionAll(int motion, bool restart)
    {
        foreach (var model in _models)
        {
            model.SetMotion(motion, restart);
        }
    }

    /// <summary>Advances every model's motion.</summary>
    /// <param name="frames">Elapsed PAL frames.</param>
    public void Step(int frames)
    {
        foreach (var model in _models)
        {
            model.Step(frames);
        }
    }

    /// <summary>Binds one light set to every model.</summary>
    /// <param name="lights">The light set.</param>
    public void SetLighting(BattleModel.BattleLightSet lights)
    {
        foreach (var model in _models)
        {
            model.SetLighting(lights);
        }
    }

    /// <summary>Restores identity scale on every bone of every model, for a legacy probe comparison.</summary>
    public void ShowAllBonesForProbe()
    {
        foreach (var model in _models)
        {
            model.ShowAllBonesForProbe();
        }
    }

    /// <summary>Swaps every model's cull mode, for a probe comparison.</summary>
    /// <param name="mode">Cull mode; callers restore Back afterwards.</param>
    public void SetCullModeForProbe(BaseMaterial3D.CullModeEnum mode)
    {
        foreach (var model in _models)
        {
            model.SetCullModeForProbe(mode);
        }
    }

    /// <summary>Shows or hides one fighter for a framebuffer mask and remembers the choice.</summary>
    /// <param name="fighterId">Fighter id, which must be built.</param>
    /// <param name="visible">True to show the fighter.</param>
    /// <exception cref="InvalidOperationException">No such fighter is built.</exception>
    public void SetVisibleForProbe(int fighterId, bool visible)
    {
        Require(fighterId).SetVisibleForProbe(visible);
        if (visible)
        {
            _probeHiddenFighters.Remove(fighterId);
        }
        else
        {
            _probeHiddenFighters.Add(fighterId);
        }
    }

    /// <summary>Applies the shot camera's per-fighter visibility, never showing a probe-hidden fighter.</summary>
    /// <param name="playerVisible">Whether the shot shows the player.</param>
    /// <param name="enemyVisible">Whether the shot shows the enemy.</param>
    public void ApplyShotVisibility(bool playerVisible, bool enemyVisible)
    {
        ShowUnlessHidden(Player, playerVisible);
        ShowUnlessHidden(Enemy, enemyVisible);
    }

    /// <summary>Shows every fighter again, except the probe-hidden ones.</summary>
    public void RestoreVisibility()
    {
        foreach (var model in _models)
        {
            ShowUnlessHidden(model, visible: true);
        }
    }

    /// <summary>Applies requested visibility unless the probe has explicitly hidden the model.</summary>
    /// <param name="model">Model to update, if present.</param>
    /// <param name="visible">Whether the current presentation state requests visibility.</param>
    private void ShowUnlessHidden(BattleModel? model, bool visible)
    {
        model?.Root.Visible = visible && !_probeHiddenFighters.Contains(model.FighterId);
    }
}
