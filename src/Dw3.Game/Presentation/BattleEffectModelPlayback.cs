using Dw3Production.Conversion;
using Dw3Production.Simulation;
using Godot;

namespace Dw3Production.Presentation;

/// <summary>Consumes extracted three-dimensional effect-model commands without substituting fighter models.</summary>
internal sealed class BattleEffectModelPlayback
{
    private readonly BattleRenderer _renderer;
    private readonly BattleDataPackLoader _loader;
    private readonly Dictionary<int, string?> _prepared = [];
    private readonly List<BattleModel> _active = [];

    /// <summary>Initializes a new instance of the <see cref="BattleEffectModelPlayback"/> class.</summary>
    /// <param name="renderer">Physical scene owner.</param>
    /// <param name="loader">Validated prepared-data loader.</param>
    public BattleEffectModelPlayback(BattleRenderer renderer, BattleDataPackLoader loader)
    {
        _renderer = renderer;
        _loader = loader;
    }

    /// <summary>Gets effect-model children that must be included in the production ordering pass.</summary>
    public IReadOnlyList<BattleModel> ActiveModels => _active;

    /// <summary>Gets a value indicating whether every instantiated effect reached its authored terminal motion.</summary>
    public bool IsComplete => _active.All(model => model.Motion?.MotionDone == true);

    /// <summary>Consumes a source load or start command.</summary>
    /// <param name="request">Validated effect-model operands.</param>
    /// <returns>Applied only after a real prepared resource is ready or source absence was qualified.</returns>
    public SequenceAcknowledgement Consume(EffectModelEvent request)
    {
        int id = request.Arguments[1];
        return request.Arguments[0] == 1 ? Preload(id) : Start(id, request.Arguments);
    }

    /// <summary>Advances each active effect through the same PAL physical clock as fighters.</summary>
    public void Step()
    {
        foreach (var model in _active)
        {
            model.Step(1);
        }
    }

    /// <summary>Frees every effect instance after normal completion or cancellation.</summary>
    public void Clear()
    {
        foreach (Node3D root in _active.Select(model => model.Root))
        {
            root.Visible = false;
            root.QueueFree();
        }

        _active.Clear();
        _prepared.Clear();
    }

    /// <summary>Applies source position and ZYX rotation with the established PS1-to-Godot conversion.</summary>
    /// <param name="root">Effect root node.</param>
    /// <param name="arguments">Start operands.</param>
    private static void ApplyTransform(Node3D root, IReadOnlyList<short> arguments)
    {
        root.Position = BattleModel.Ps1Position(new Vector3(arguments[2], arguments[3], arguments[4]));
        root.Rotation = new Vector3(-arguments[5], arguments[6], -arguments[7]) * Mathf.Tau / 4096f;
    }

    /// <summary>Resolves and retains a registry path, preserving a qualified absent source id as null.</summary>
    /// <param name="id">Script-visible effect-model id.</param>
    /// <returns>Applied when the registry lookup completed.</returns>
    private SequenceAcknowledgement Preload(int id)
    {
        if (!_prepared.ContainsKey(id))
        {
            _prepared.Add(id, _loader.EffectModelPath(id));
        }

        return SequenceAcknowledgement.Applied;
    }

    /// <summary>Builds one independent instance so repeated starts of the same id never share motion state.</summary>
    /// <param name="id">Script-visible effect-model id.</param>
    /// <param name="arguments">Start operands: subcommand, id, position and rotation.</param>
    /// <returns>Applied after a visible instance is attached, or immediately for a qualified source absence.</returns>
    private SequenceAcknowledgement Start(int id, IReadOnlyList<short> arguments)
    {
        Preload(id);
        if (_prepared[id] is not { } path)
        {
            return SequenceAcknowledgement.Applied;
        }

        BattleModel model = _renderer.LoadSequenceEffect(path);
        ApplyTransform(model.Root, arguments);
        _active.Add(model);
        return SequenceAcknowledgement.Applied;
    }
}
