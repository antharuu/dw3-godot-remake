using Dw3Production.Conversion;
using Dw3Production.Simulation;
using Godot;

namespace Dw3Production.Presentation;

/// <summary>Owns the source contact sprite children and the unchanged-stage cases of a basic attack.</summary>
internal sealed class BattleImpactPlayback
{
    private readonly CanvasLayer _layer;
    private readonly Camera3D _camera;
    private readonly BattleDataPackLoader _loader;
    private readonly List<(BattleImpactVisual Visual, Vector3 Anchor)> _children = [];
    private int _stage = -1;

    /// <summary>Initializes a new instance of the <see cref="BattleImpactPlayback"/> class.</summary>
    /// <param name="layer">Production presentation layer.</param>
    /// <param name="camera">Production camera used to project contact coordinates.</param>
    /// <param name="loader">Validated local pack.</param>
    public BattleImpactPlayback(CanvasLayer layer, Camera3D camera, BattleDataPackLoader loader)
    {
        _layer = layer;
        _camera = camera;
        _loader = loader;
    }

    /// <summary>Gets a value indicating whether every contact child finished.</summary>
    public bool IsComplete => _children.All(child => child.Visual.IsComplete);

    /// <summary>Freezes the optional stage before playback starts.</summary>
    /// <param name="context">Frozen action metadata.</param>
    public void Begin(ActionSequenceContext context) => _stage = context.ScriptStage;

    /// <summary>Consumes only qualified contact and unchanged-stage requests.</summary>
    /// <param name="request">Stable action request.</param>
    /// <returns>Applied for a real contact child or source no-change, unavailable otherwise.</returns>
    public SequenceAcknowledgement Consume(BattleSequenceEvent request) => request switch
    {
        StageChangeEvent stage => ConsumeStage(stage),
        SpriteEffectEvent sprite => ConsumeSprite(sprite),
        _ => SequenceAcknowledgement.Unavailable,
    };

    /// <summary>Advances retained contact drawings and updates their live camera projection.</summary>
    public void Step()
    {
        foreach (var child in _children)
        {
            child.Visual.Step();
            child.Visual.Position = _camera.UnprojectPosition(child.Anchor);
        }
    }

    /// <summary>Removes all retained geometry on completion or cancellation.</summary>
    public void Clear()
    {
        foreach (var visual in _children.Select(child => child.Visual))
        {
            visual.Visible = false;
            visual.QueueFree();
        }

        _children.Clear();
    }

    /// <summary>Handles no optional stage and return to the unchanged battle stage without inventing a fade.</summary>
    /// <param name="request">Validated stage request.</param>
    /// <returns>Unavailable when a different stage requires its own renderer.</returns>
    private SequenceAcknowledgement ConsumeStage(StageChangeEvent request)
    {
        int id = request.Arguments[1];
        int resolved = id == 0x38 ? _stage : id;
        return resolved is -1 or 0 ? SequenceAcknowledgement.Applied : SequenceAcknowledgement.Unavailable;
    }

    /// <summary>Preloads or starts the recovered common contact effect.</summary>
    /// <param name="request">Validated sprite request.</param>
    /// <returns>Applied only when the required manifest or drawing exists.</returns>
    private SequenceAcknowledgement ConsumeSprite(SpriteEffectEvent request)
    {
        var a = request.Arguments;
        if (a[1] != 2)
        {
            return SequenceAcknowledgement.Unavailable;
        }

        string manifest = _loader.ImpactPath();
        if (a[0] == 1)
        {
            return SequenceAcknowledgement.Applied;
        }

        var anchor = new Vector3(a[2], -a[3], -a[4]) / BattleSceneMath.Ps1UnitsPerGodotUnit;
        var visual = new BattleImpactVisual(manifest) { Position = _camera.UnprojectPosition(anchor), Scale = Vector2.One * 3f };
        _layer.AddChild(visual);
        _children.Add((visual, anchor));
        return SequenceAcknowledgement.Applied;
    }
}
