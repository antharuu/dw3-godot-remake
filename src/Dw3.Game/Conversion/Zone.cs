namespace Dw3Production.Conversion;

/// <summary>The complete runtime contract for one field zone: terrain layers, collision maps,
/// warp exits, climb slots, player animation source hash and optional area map.</summary>
public sealed record Zone(string Id, int Width, int Height, IReadOnlyList<Layer> Layers, string FloorCellsPath, string TriggerCellsPath, IReadOnlyList<FieldExit> Exits, IReadOnlyList<Climb> Climbs, string SourceHash, StreamModulate? Depth11Modulate = null, string Status = "loadable", IReadOnlyList<Drop>? Drops = null, string? AreasCellsPath = null);
