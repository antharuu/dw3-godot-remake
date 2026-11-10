namespace Dw3.Formats.Battle;

/// <summary>One decoded command from a fighter action-script archive entry.</summary>
/// <param name="Opcode">The semantic opcode.</param>
/// <param name="Arguments">The signed 16-bit arguments in source order.</param>
public sealed record BattleActionCommand(BattleActionOpcode Opcode, IReadOnlyList<short> Arguments);
