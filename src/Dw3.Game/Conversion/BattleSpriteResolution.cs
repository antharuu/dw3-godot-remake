namespace Dw3Production.Conversion;

/// <summary>The outcome of resolving one sprite identity against the prepared index.</summary>
/// <param name="Id">Requested script-visible identity.</param>
/// <param name="Availability">Explicit availability, never inferred from a missing row.</param>
/// <param name="Sheet">Renderable sheet when available.</param>
/// <param name="Reason">Qualification of an unavailable identity.</param>
public sealed record BattleSpriteResolution(int Id, BattleSpriteAvailability Availability, BattleSpriteSheet? Sheet, string? Reason);
