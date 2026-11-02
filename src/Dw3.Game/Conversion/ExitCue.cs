namespace Dw3Production.Conversion;

/// <summary>The exit-cue sprite frames and the inner two-frame loop of each field row.</summary>
public sealed record ExitCue(IReadOnlyDictionary<int, CueFrame> Frames, IReadOnlyDictionary<int, IReadOnlyList<(int Frame, int Duration)>> InnerRows);
