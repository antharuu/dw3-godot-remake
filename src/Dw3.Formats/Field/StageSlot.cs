namespace Dw3.Formats.Field;

/// <summary>One disc-backed StageSlot record, including its terminating type-zero record.</summary>
public sealed record StageSlot(ushort Condition, ushort Type, ushort Argument, ushort X, ushort Y, ushort Direction);
