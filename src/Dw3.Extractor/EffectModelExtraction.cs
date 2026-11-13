using Dw3.Formats.Battle;

namespace Dw3.Extractor;

/// <summary>One extracted shared effect model with its decoded payload and source provenance.</summary>
/// <param name="Reference">Script-visible identifier and source archive references.</param>
/// <param name="Payload">Decoded meshes, textures, tracks and motions.</param>
/// <param name="DiscPath">ISO path of the model archive.</param>
/// <param name="Sha256">SHA-256 of the model archive bytes.</param>
internal sealed record EffectModelExtraction(EffectModelReference Reference, BattleModelPayload Payload, string DiscPath, string Sha256);
