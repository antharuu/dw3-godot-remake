using Dw3.Formats.Battle;

namespace Dw3.Extractor;

/// <summary>
/// One fighter-table entry after extraction: its outcome and, when decoded, the model structure
/// and the disc member it was read from.
/// </summary>
/// <param name="Outcome">The per-entry decode status and source reference.</param>
/// <param name="Model">Decoded mesh structure when the entry completed.</param>
/// <param name="DiscPath">ISO path of the model file, null for a failed entry.</param>
/// <param name="Sha256">SHA-256 of the model file, null for a failed entry.</param>
/// <param name="Payload">Optional textures, tracks, motions, and facial metadata.</param>
public sealed record BattleModelExtraction(BattleModelOutcome Outcome, BattleModelStructure? Model, string? DiscPath, string? Sha256, BattleModelPayload? Payload = null);
