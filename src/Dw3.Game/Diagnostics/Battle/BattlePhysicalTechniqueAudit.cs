using Dw3.Data.Battle;
using Dw3Production.Diagnostics.Field;
using Dw3Production.Simulation;
using Godot;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Asserts per-hit physical consumers and restoration without replacing their behaviour.</summary>
internal sealed class BattlePhysicalTechniqueAudit
{
    private readonly IBattleProbeHost _host;
    private readonly ActionSequenceContext _context;
    private readonly BattleActionScript _script;
    private readonly List<int> _completed = [];
    private readonly List<BattleSequenceEvent> _requests = [];
    private readonly Dictionary<Dw3Production.Presentation.BattleModel, Vector3> _homes;
    private readonly Transform3D _camera;
    private readonly Dw3Production.Presentation.BattleModel? _arena;
    private readonly (BattleFighter Fighter, int Hp, int Mp)[] _state;
    private bool _optionalStageObserved;

    /// <summary>Initializes a new instance of the <see cref="BattlePhysicalTechniqueAudit"/> class.</summary>
    /// <param name="host">Real offscreen host.</param>
    /// <param name="context">Frozen simulation outcome.</param>
    /// <param name="script">Actual provider-loaded commands.</param>
    public BattlePhysicalTechniqueAudit(IBattleProbeHost host, ActionSequenceContext context, BattleActionScript script)
    {
        _host = host;
        _context = context;
        _script = script;
        _homes = host.Renderer.SequencePlayback.DiagnosticRoster.Models.ToDictionary(model => model, model => model.Root.Position);
        _camera = host.Renderer.CameraForProbe.Transform;
        _arena = host.Renderer.GetSequenceStage();
        _state = [.. host.Flow.State.Players.Concat(host.Flow.State.Enemies).OfType<BattleFighter>().Select(fighter => (fighter, fighter.Hp, fighter.Mp))];
        host.Renderer.SequencePlayback.DiagnosticConsumed = Consume;
        host.Renderer.SequencePlayback.DiagnosticReaction = Reaction;
    }

    /// <summary>Requires every real hit reaction to finish in order, all source dispatches and physical return.</summary>
    public void Verify()
    {
        ProbeRequirement.Require(_completed.SequenceEqual(_context.HitEvents.Select(hit => hit.HitIndex)), $"lost reaction completion: {string.Join(',', _completed)}");
        var expected = _script.Commands.Where(command => command.Opcode == BattleActionOpcode.HitDispatch).Select(command => (int)command.Arguments[0]);
        ProbeRequirement.Require(_requests.OfType<HitDispatchedEvent>().Select(hit => hit.DispatchIndex).SequenceEqual(expected), "dispatch order changed");
        int sprites = _script.Commands.Count(command => command.Opcode == BattleActionOpcode.SpriteEffect && command.Arguments[0] == 0);
        ProbeRequirement.Require(_requests.OfType<SpriteEffectEvent>().Count(sprite => sprite.Arguments[0] == 0) == sprites, "authored contacts not consumed");
        ProbeRequirement.Require(_homes.All(pair => pair.Key.Root.Position.IsEqualApprox(pair.Value)), "physical homes not restored");
        ProbeRequirement.Require(ReferenceEquals(_arena, _host.Renderer.GetSequenceStage()), "retained arena not restored");
        ProbeRequirement.Require(_context.ScriptStage < 0 || _optionalStageObserved, "optional stage never physically displayed");
        ProbeRequirement.Require(_host.Renderer.CameraForProbe.Transform.IsEqualApprox(_camera), $"camera not restored: expected={_camera} actual={_host.Renderer.CameraForProbe.Transform}");
        ProbeRequirement.Require(_state.All(item => item.Fighter.Hp == item.Hp && item.Fighter.Mp == item.Mp), "playback changed HP/MP");
        _host.Renderer.SequencePlayback.DiagnosticConsumed = null;
        _host.Renderer.SequencePlayback.DiagnosticReaction = null;
    }

    /// <summary>Observes requests only after their production consumer acknowledges them.</summary>
    /// <param name="request">Actual physically applied request.</param>
    private void Consume(BattleSequenceEvent request)
    {
        _requests.Add(request);
        _optionalStageObserved |= _context.ScriptStage >= 0 && _host.Renderer.SequencePlayback.DiagnosticStage == _context.ScriptStage;
        GD.Print($"B9053_CONSUMED {System.Text.Json.JsonSerializer.Serialize(request, request.GetType())}");
        if (request is HitDispatchedEvent hit)
        {
            ProbeRequirement.Require(hit.TargetId == _context.TargetId && hit.HitEvents.SequenceEqual(_context.HitEvents), "dispatch retargeted or rerolled");
        }
    }

    /// <summary>Records actual child completion, not merely receipt of a list of hits.</summary>
    /// <param name="hit">Consumed frozen hit.</param>
    /// <param name="target">Actual target script fighter.</param>
    /// <param name="selector">Actual reaction selector.</param>
    /// <param name="complete">Whether its physical child reached End.</param>
    private void Reaction(BattleHitEvent hit, int target, int selector, bool complete)
    {
        GD.Print($"B9053_REACTION hit={hit.HitIndex} target={target} selector={selector} complete={complete}");
        ProbeRequirement.Require(target == _context.TargetId, "reaction retargeted");
        if (complete)
        {
            _completed.Add(hit.HitIndex);
        }
    }
}
