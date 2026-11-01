using Dw3.Formats.Water;

namespace Dw3.Extractor;

/// <summary>A water stage whose map objects decoded successfully from the disc.</summary>
/// <param name="StageId">Numeric stage identifier used to locate the stage files on disc.</param>
/// <param name="Objects">Decoded set of map objects and their animation frames.</param>
/// <param name="Sources">SHA-256 digests of the disc files consumed during decoding.</param>
public sealed record ExtractedWaterZone(int StageId, StageObjectSet Objects, WaterZoneSources Sources);
