using Dw3Production.Conversion;
using Dw3Production.Simulation;

namespace Dw3Production.Presentation;

/// <summary>Retains the encounter arena while source action commands preload and fade an optional stage.</summary>
internal sealed class BattleSequenceStagePlayback
{
    private readonly BattleRenderer _renderer;
    private readonly BattleDataPackLoader _loader;
    private readonly Dictionary<int, BattleModel> _loaded = [];
    private BattleModel? _original;
    private BattleModel? _next;
    private StageChangeEvent? _request;
    private int _optional;
    private int _tick;
    private int _out;
    private int _in;

    /// <summary>Initializes a new instance of the <see cref="BattleSequenceStagePlayback"/> class.</summary>
    /// <param name="renderer">Physical scene owner.</param>
    /// <param name="loader">Validated local stage registry.</param>
    public BattleSequenceStagePlayback(BattleRenderer renderer, BattleDataPackLoader loader)
    {
        _renderer = renderer;
        _loader = loader;
    }

    /// <summary>Gets the currently displayed optional stage, or zero for the retained encounter arena.</summary>
    public int ActiveStage { get; private set; }

    /// <summary>Captures the actual encounter model, not a rebuilt approximation.</summary>
    /// <param name="context">Frozen optional-stage metadata.</param>
    public void Begin(ActionSequenceContext context)
    {
        _original = _renderer.GetSequenceStage();
        _optional = context.ScriptStage;
    }

    /// <summary>Preloads source stages or blocks on the real two-part fade before acknowledging a swap.</summary>
    /// <param name="request">Stable stage command retained by the timeline.</param>
    /// <returns>Applied only after preload or the complete physical transition.</returns>
    public SequenceAcknowledgement Consume(StageChangeEvent request)
    {
        int id = request.Arguments[1] == 56 ? _optional : request.Arguments[1];
        if (id == -1 || (id == 0 && ActiveStage == 0))
        {
            return SequenceAcknowledgement.Applied;
        }

        var model = id == 0 ? _original : Load(id);
        if (model is null)
        {
            return SequenceAcknowledgement.Unavailable;
        }

        if (request.Arguments[0] == 1)
        {
            return SequenceAcknowledgement.Applied;
        }

        if (_request is null)
        {
            _request = request;
            _next = model;
            _tick = 0;
            _out = request.Arguments[2];
            _in = request.Arguments[3];
        }

        if (_tick < _out + _in)
        {
            return SequenceAcknowledgement.Pending;
        }

        _renderer.SwapSequenceStage(model);
        _renderer.SetSequenceStageFade(1);
        ActiveStage = id;
        _request = null;
        return SequenceAcknowledgement.Applied;
    }

    /// <summary>Advances the source fade on the same physical clock as the action's actors.</summary>
    public void Step()
    {
        if (_request is null || _next is null)
        {
            return;
        }

        _tick++;
        if (_tick < _out)
        {
            _renderer.SetSequenceStageFade(1f - ((float)_tick / _out));
            return;
        }

        _renderer.SwapSequenceStage(_next);
        _renderer.SetSequenceStageFade(_in == 0 ? 1 : Math.Min(1, (float)(_tick - _out) / _in));
    }

    /// <summary>Restores the exact retained encounter model on either End or cancellation and frees optional models.</summary>
    public void Clear()
    {
        if (_original is not null)
        {
            _renderer.SwapSequenceStage(_original);
            _renderer.SetSequenceStageFade(1);
        }

        foreach (var root in _loaded.Values.Select(model => model.Root))
        {
            root.Visible = false;
            root.QueueFree();
        }

        _loaded.Clear();
        _request = null;
        _next = null;
        _original = null;
        ActiveStage = 0;
    }

    /// <summary>Loads each optional model once without displaying a preload.</summary>
    /// <param name="id">Decoded stage registry identifier.</param>
    /// <returns>The retained hidden model.</returns>
    private BattleModel Load(int id)
    {
        if (!_loaded.TryGetValue(id, out var model))
        {
            model = _renderer.LoadSequenceStage(_loader.StagePath(id));
            model.Root.Visible = false;
            _loaded.Add(id, model);
        }

        return model;
    }
}
