using Dw3Production.Conversion;
using Dw3Production.Simulation;
using Godot;

namespace Dw3Production.Presentation;

/// <summary>Consumes real fighter-spawn, root-rotation and deletion commands in action-local slots.</summary>
internal sealed class BattleSequenceActorPlayback
{
    private readonly BattleRenderer _renderer;
    private readonly BattleFighterRoster _fighters;
    private readonly BattleDataPackLoader _loader;
    private readonly Dictionary<BattleModel, Vector3> _homes = [];
    private readonly Dictionary<BattleModel, BattleTravelMotion> _rotations = [];

    /// <summary>Initializes a new instance of the <see cref="BattleSequenceActorPlayback"/> class.</summary>
    /// <param name="renderer">Physical scene owner.</param>
    /// <param name="fighters">Retained active and action-local fighters.</param>
    /// <param name="loader">Validated fighter model registry.</param>
    public BattleSequenceActorPlayback(BattleRenderer renderer, BattleFighterRoster fighters, BattleDataPackLoader loader)
    {
        _renderer = renderer;
        _fighters = fighters;
        _loader = loader;
    }

    /// <summary>Consumes model commands without treating additional fighters as effect resources.</summary>
    /// <param name="request">Frozen side and raw operands.</param>
    /// <returns>The physical owner's acknowledgement.</returns>
    public SequenceAcknowledgement Consume(ModelControlEvent request)
    {
        var a = request.Arguments;
        bool enemy = request.AttackerSide == BattleSide.Enemy;
        switch (a[0])
        {
            case 6:
                var model = _renderer.LoadSequenceActor(_loader.ModelPath(a[2]));
                _fighters.AddSequenceActor(enemy, a[1], model);
                _renderer.RefreshSequenceLighting();
                return SequenceAcknowledgement.Applied;
            case 9:
                return Delete(enemy, a[1]);
            case 4:
                return Rotate(enemy, a);
            default:
                return SequenceAcknowledgement.Unavailable;
        }
    }

    /// <summary>Advances independently retained root-rotation transitions.</summary>
    public void Step()
    {
        foreach (var pair in _rotations)
        {
            pair.Value.Step();
            pair.Key.Root.Rotation = pair.Value.Position;
        }
    }

    /// <summary>Restores original actor orientations and destroys all additional models on End or cancellation.</summary>
    public void Clear()
    {
        foreach (var pair in _homes)
        {
            pair.Key.Root.Rotation = pair.Value;
        }

        _homes.Clear();
        _rotations.Clear();
        _fighters.ClearSequenceActors();
    }

    /// <summary>Applies the qualified Y-only rotations from the recovered additional-fighter scripts.</summary>
    /// <param name="enemy">Owning script side.</param>
    /// <param name="arguments">Raw rotation operands.</param>
    /// <returns>Unavailable for a missing actor or an unqualified multi-axis composition.</returns>
    private SequenceAcknowledgement Rotate(bool enemy, IReadOnlyList<short> arguments)
    {
        var model = _fighters.SequenceActorModel(enemy, arguments[1]);
        if (model is null || arguments[2] != 0 || arguments[4] != 0)
        {
            return SequenceAcknowledgement.Unavailable;
        }

        _homes.TryAdd(model, model.Root.Rotation);
        var target = new Vector3(0, arguments[3] * Mathf.Tau / 4096f, 0);
        var rotation = new BattleTravelMotion(model.Root.Rotation, target, arguments[5]);
        _rotations[model] = rotation;
        model.Root.Rotation = rotation.Position;
        return SequenceAcknowledgement.Applied;
    }

    /// <summary>Releases every retained rotation reference before deleting an action-local model.</summary>
    /// <param name="enemy">Owning script side.</param>
    /// <param name="actor">Additional actor slot.</param>
    /// <returns>Unavailable if the required actor was never loaded.</returns>
    private SequenceAcknowledgement Delete(bool enemy, int actor)
    {
        var model = _fighters.SequenceActorModel(enemy, actor);
        if (model is null)
        {
            return SequenceAcknowledgement.Unavailable;
        }

        _homes.Remove(model);
        _rotations.Remove(model);
        _fighters.RemoveSequenceActor(enemy, actor);
        return SequenceAcknowledgement.Applied;
    }
}
