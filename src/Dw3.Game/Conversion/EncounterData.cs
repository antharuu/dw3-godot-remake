namespace Dw3Production.Conversion;

/// <summary>The m6-encounter-v1 contract: battle rates, the enemy pool, every encounter and per-stage battles.</summary>
public sealed record EncounterData(IReadOnlyList<int> BattleRates, IReadOnlyList<BattleEnemy> Enemies, IReadOnlyList<EncounterEntry> Encounters, IReadOnlyDictionary<string, EncounterStage> Stages);
