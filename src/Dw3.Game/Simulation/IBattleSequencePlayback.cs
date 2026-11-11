namespace Dw3Production.Simulation;

/// <summary>Physical playback seam. Pending requests must not create duplicate children on retries.</summary>
public interface IBattleSequencePlayback
{
    /// <summary>Gets a value indicating whether reaction and sprite children have completed.</summary>
    bool ChildrenReady { get; }

    /// <summary>Acquires presentation ownership for a frozen action.</summary>
    /// <param name="context">Frozen action.</param>
    void Begin(ActionSequenceContext context);

    /// <summary>Tests the selected physical model's terminal motion marker.</summary>
    /// <param name="actor">Script actor selector.</param>
    /// <returns>True only when the model is present and its motion completed.</returns>
    bool MotionReady(int actor);

    /// <summary>Consumes a request or reports pending/unavailable without claiming it was applied.</summary>
    /// <param name="request">Typed request.</param>
    /// <returns>Explicit acknowledgement.</returns>
    SequenceAcknowledgement Consume(BattleSequenceEvent request);

    /// <summary>Releases ownership, cancels children and restores presentation state.</summary>
    /// <param name="cancelled">Whether playback was interrupted.</param>
    void Finish(bool cancelled);
}
