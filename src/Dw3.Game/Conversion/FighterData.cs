namespace Dw3Production.Conversion;

/// <summary>The m6-fighter-v1 contract: DIGIMON_DATA keyed by id, the same list in its source
/// array order (DIGIMON_DATA[0..54], so "the first 8 are the partners" — game_state.h), plus
/// the enemy battle table. The ordered list lets the starter party resolve partner i to
/// DIGIMON_DATA[i].id without inventing a mapping.</summary>
public sealed record FighterData(IReadOnlyDictionary<int, DigimonData> Digimon, IReadOnlyList<DigimonData> DigimonOrdered, IReadOnlyList<BattleTableEntry> BattleTable);
