using Dw3Production.Conversion;

namespace Dw3Production.Simulation;

/// <summary>
/// All runtime data for one loaded field zone: the zone definition, its floor, trigger and
/// area maps, and the list of occupying actors. Occupants and Areas are optional; a zone
/// without an area map triggers no field battles.
/// </summary>
public sealed record FieldZone(Zone Zone, FloorMap Floor, TriggerMap Triggers, IReadOnlyList<Occupant> Occupants, AreaMap? Areas = null);
