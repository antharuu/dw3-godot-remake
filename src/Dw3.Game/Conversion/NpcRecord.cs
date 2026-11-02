namespace Dw3Production.Conversion;

/// <summary>A visible city NPC: source position/direction/width plus its resolved idle loop.</summary>
public sealed record NpcRecord(string ActorId, int X, int Y, int Width, int SpriteDirection, bool FlipH, IReadOnlyList<NpcFrame> IdleFrames);
