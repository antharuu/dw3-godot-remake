namespace Dw3.Formats.Field;

/// <summary>One decoded visibility condition from an actor's condition list.</summary>
/// <param name="Code">Condition kind encoded as a 16-bit word.</param>
/// <param name="Value">Comparison operand for the condition code.</param>
public sealed record NpcCondition(ushort Code, ushort Value);
