namespace Dw3.Formats.Field;

/// <summary>A raw overlay coordinate and its verified, decoded StageSlot records.</summary>
public sealed record StageSlotTable(int Offset, IReadOnlyList<StageSlot> Slots);
