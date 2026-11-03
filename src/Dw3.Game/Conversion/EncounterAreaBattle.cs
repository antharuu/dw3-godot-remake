namespace Dw3Production.Conversion;

/// <summary>One battle of a field area's battle list: the encounter index, fight stage and battle music.</summary>
public sealed record EncounterAreaBattle(int Encounter, int Stage, int Music, string MusicName);
