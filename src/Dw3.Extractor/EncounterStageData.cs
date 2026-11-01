using Dw3.Formats.Encounter;

namespace Dw3.Extractor;

/// <summary>
/// Groups one stage overlay identity with the battle arrays decoded from that overlay.
/// </summary>
/// <param name="OverlayName">Gets the ISO file name used by the parity manifest.</param>
/// <param name="OverlaySha256">Gets the lowercase SHA-256 digest of the source overlay bytes.</param>
/// <param name="Arrays">Gets the battle arrays in their decoded source order.</param>
internal sealed record EncounterStageData(
    string OverlayName,
    string OverlaySha256,
    IReadOnlyList<StageBattleArray> Arrays);
