namespace Dw3Production.Conversion;

/// <summary>The connection-indicator frames 0x23..0x31 and the animation tables keyed by substate.</summary>
public sealed record PlayerIconData(IReadOnlyDictionary<int, PlayerIconFrame> Frames, IReadOnlyDictionary<int, PlayerIconAnimation> Animations);
