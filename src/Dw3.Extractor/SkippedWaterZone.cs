namespace Dw3.Extractor;

/// <summary>A water stage whose map-object files use a layout the decoder rejects.</summary>
/// <param name="StageId">Numeric stage identifier used to locate the stage files on disc.</param>
/// <param name="Error">Human-readable description of the decoding failure.</param>
public sealed record SkippedWaterZone(int StageId, string Error);
