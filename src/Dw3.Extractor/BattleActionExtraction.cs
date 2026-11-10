using Dw3.Formats.Battle;

namespace Dw3.Extractor;

/// <summary>One fighter action archive decoded for the runtime data pack.</summary>
/// <param name="FighterId">Fighter table identifier owning the archive.</param>
/// <param name="EffectFile">Engine file id holding the archive.</param>
/// <param name="EffectEntry">Archive entry selected by the fighter record.</param>
/// <param name="Script">Decoded bounded action script, when available.</param>
/// <param name="Reason">Explicit decoding failure when the first-batch grammar does not cover the entry.</param>
internal sealed record BattleActionExtraction(int FighterId, int EffectFile, int EffectEntry, BattleActionScript? Script, string? Reason);
