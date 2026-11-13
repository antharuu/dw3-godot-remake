namespace Dw3Production.Conversion;

/// <summary>One authored change of the global reverb state, ordered with the sequence's note events.</summary>
/// <param name="Parameter">Changed reverb attribute.</param>
/// <param name="Value">Source data-entry value: preset index for type, 0-127 for depth.</param>
public sealed record AudioReverbChange(AudioReverbParameter Parameter, int Value);
