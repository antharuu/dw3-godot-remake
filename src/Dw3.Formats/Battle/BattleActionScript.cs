namespace Dw3.Formats.Battle;

/// <summary>One bounded, decoded entry from a fighter effect-script archive.</summary>
/// <param name="Entry">Zero-based archive entry used by the technique selector.</param>
/// <param name="Commands">Decoded commands, including the required end command.</param>
public sealed record BattleActionScript(int Entry, IReadOnlyList<BattleActionCommand> Commands);
