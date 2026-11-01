namespace Dw3Production.Presentation;

/// <summary>Immutable snapshot of the exit-cue balloon state for one simulation step.</summary>
public sealed record ExitCueSnapshot(ExitCuePhase Phase, int PopFrame, int? InnerFrame, int RawDirection)
{
    /// <summary>Gets a value indicating whether the balloon is visible (any phase other than Hidden).</summary>
    public bool Visible => Phase is not ExitCuePhase.Hidden;
}
