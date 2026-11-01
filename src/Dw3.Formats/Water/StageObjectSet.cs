namespace Dw3.Formats.Water;

/// <summary>The drawable map objects of one stage and the number of object VRAM pages loaded.</summary>
/// <param name="Objects">The map objects in source table order.</param>
/// <param name="ArchiveEntries">The number of decoded VRAM pages.</param>
public sealed record StageObjectSet(IReadOnlyList<StageObject> Objects, int ArchiveEntries);
