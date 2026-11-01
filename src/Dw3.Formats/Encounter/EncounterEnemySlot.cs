namespace Dw3.Formats.Encounter;

/// <summary>
/// One of 3 enemy slots within an Encounter record: carries the enemy table index plus its
/// resolved BattleEnemy snapshot.
/// </summary>
public sealed record EncounterEnemySlot(int EnemyIndex, BattleEnemy Enemy);
