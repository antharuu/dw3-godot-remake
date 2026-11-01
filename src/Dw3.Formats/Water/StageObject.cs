namespace Dw3.Formats.Water;

/// <summary>A drawable map object and every frame its animation cycle can show.</summary>
/// <param name="Index">Position of the record in the StageTile table.</param>
/// <param name="Tile">The decoded StageTile record.</param>
/// <param name="Frames">Frames in cycle order: palette rows outermost, frame ids innermost.</param>
public sealed record StageObject(int Index, StageTile Tile, IReadOnlyList<StageObjectFrame> Frames);
