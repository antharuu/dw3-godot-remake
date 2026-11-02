using NumericsVector2 = System.Numerics.Vector2;

namespace Dw3Production.Startup;

/// <summary>Where and how a session begins.</summary>
/// <param name="ZoneId">Normalized zone id (<c>Snnn</c>) the player starts in.</param>
/// <param name="Position">Player position in world units.</param>
/// <param name="Facing">Player facing, one of the eight source directions 0..7.</param>
/// <param name="IsDiagnostic">
/// <see langword="true"/> when the zone was chosen through <c>DW3_START_ZONE</c> for map inspection
/// rather than reached through the game's own spawn.
/// </param>
internal sealed record SessionStart(string ZoneId, NumericsVector2 Position, int Facing, bool IsDiagnostic);
