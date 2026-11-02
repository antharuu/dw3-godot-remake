namespace Dw3Production.Conversion;

/// <summary>One exit-cue sprite frame and its draw offset.</summary>
public sealed record CueFrame(int Frame, string Path, int OffsetX, int OffsetY);
