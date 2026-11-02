namespace Dw3Production.Conversion;

/// <summary>The player animation contract: all sequences from the manifest and the render anchor offset.</summary>
public sealed record PlayerData(IReadOnlyList<PlayerSequence> Sequences, int AnchorX, int AnchorY);
