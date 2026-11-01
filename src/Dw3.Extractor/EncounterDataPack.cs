using Dw3.Formats.Encounter;

namespace Dw3.Extractor;

/// <summary>
/// Keeps the decoded global tables and stage overlays together while they are written in one data-pack transaction.
/// </summary>
/// <param name="Enemies">Contains enemy rows in FIELDSTG.PRO table order.</param>
/// <param name="Encounters">Contains encounter rows in FIELDSTG.PRO table order.</param>
/// <param name="BattleRates">Contains the field battle-rate table in source order.</param>
/// <param name="Stages">Maps stage identifiers to arrays discovered in the matching overlay.</param>
/// <param name="FieldStgIsoPath">Contains the source member path recorded in the parity manifest.</param>
/// <param name="FieldStgSha256">Contains the lowercase digest of that source member.</param>
internal sealed record EncounterDataPack(
    IReadOnlyList<BattleEnemy> Enemies,
    IReadOnlyList<EncounterRecord> Encounters,
    IReadOnlyList<int> BattleRates,
    IReadOnlyDictionary<int, EncounterStageData> Stages,
    string FieldStgIsoPath,
    string FieldStgSha256);
