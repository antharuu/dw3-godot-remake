namespace Dw3Production.Conversion;

/// <summary>A tile-occupying actor used by the free-tile test of locomotion: zone id, runtime actor id,
/// tile-centre position and half-width.</summary>
public sealed record CollisionActor(string ZoneId, string ActorId, int X, int Y, int Width);
