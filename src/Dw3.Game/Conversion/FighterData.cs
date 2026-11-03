namespace Dw3Production.Conversion;

/// <summary>The fighter data contract (schema <c>m6-fighter-v1</c>): the Digimon table keyed by id, the same
/// list in its disc order (entries 0..54, the first eight being the partners), plus
/// the enemy fighter table. The ordered list lets the starter party resolve partner i to
/// the id of table entry i without inventing a mapping.</summary>
public sealed record FighterData(IReadOnlyDictionary<int, DigimonData> Digimon, IReadOnlyList<DigimonData> DigimonOrdered, IReadOnlyList<BattleTableEntry> BattleTable);
