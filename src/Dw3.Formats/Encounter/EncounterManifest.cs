namespace Dw3.Formats.Encounter;

/// <summary>
/// The fully decoded encounter domain for one supported disc: enemies, encounters, battle rates,
/// and per-stage battle arrays.
/// </summary>
public sealed record EncounterManifest(
    IReadOnlyList<BattleEnemy> Enemies,
    IReadOnlyList<EncounterRecord> Encounters,
    IReadOnlyList<int> BattleRates,
    IReadOnlyDictionary<int, IReadOnlyList<StageBattleArray>> Stages);
