namespace Dw3Production.Simulation;

/// <summary>
/// A field encounter the controller resolved. Kind is "area" (FIELDSTG_startAreaBattle) or
/// "event" (FIELDSTG_startEventBattle); Area is the zero-based battle area (always 3 for
/// events); Index is the battle slot 0..7.
/// </summary>
public sealed record EncounterRecord(string Kind, int Area, int Index, AreaBattle Battle);
