namespace Dw3Production.Conversion;

/// <summary>One AreaBattle (field_map.h): the encounter index, fight stage and battle music.</summary>
public sealed record EncounterAreaBattle(int Encounter, int Stage, int Music, string MusicName);
