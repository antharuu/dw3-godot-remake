using Dw3Production.Simulation;

namespace Dw3Production.Presentation;

/// <summary>Owns the unchanged-stage cases of a basic attack: no optional stage, or the return to the battle stage.</summary>
internal sealed class BattleImpactPlayback
{
    private int _stage = -1;

    /// <summary>Freezes the optional stage before playback starts.</summary>
    /// <param name="context">Frozen action metadata.</param>
    public void Begin(ActionSequenceContext context) => _stage = context.ScriptStage;

    /// <summary>Consumes only unchanged-stage requests.</summary>
    /// <param name="request">Stable action request.</param>
    /// <returns>Applied for a source no-change, unavailable otherwise.</returns>
    public SequenceAcknowledgement Consume(BattleSequenceEvent request) => request is StageChangeEvent stage
        ? ConsumeStage(stage) : SequenceAcknowledgement.Unavailable;

    /// <summary>Forgets the frozen stage on completion or cancellation.</summary>
    public void Clear() => _stage = -1;

    /// <summary>Handles no optional stage and return to the unchanged battle stage without inventing a fade.</summary>
    /// <param name="request">Validated stage request.</param>
    /// <returns>Unavailable when a different stage requires its own renderer.</returns>
    private SequenceAcknowledgement ConsumeStage(StageChangeEvent request)
    {
        int id = request.Arguments[1];
        int resolved = id == 0x38 ? _stage : id;
        return resolved is -1 or 0 ? SequenceAcknowledgement.Applied : SequenceAcknowledgement.Unavailable;
    }
}
