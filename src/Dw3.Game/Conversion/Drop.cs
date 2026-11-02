namespace Dw3Production.Conversion;

/// <summary>A SLOT_DROP (Type 4) slot. Height is arg * 16 source pixels; direction is read from the trigger cell.</summary>
public sealed record Drop(int Slot, int Height);
