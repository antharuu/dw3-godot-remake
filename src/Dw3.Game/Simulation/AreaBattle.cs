namespace Dw3Production.Simulation;

/// <summary>
/// One battle entry from a field area's BattleList (field_map.h AreaBattle). Encounter is
/// the FIELDSTG_encounters index, Stage is the fight stage, Music is the battle music id.
/// The empty slots keep the bare { 0, 0, MUSIC(1, 0) }.
/// </summary>
public sealed record AreaBattle(int Encounter, int Stage, int Music);
