namespace Dw3Production.Conversion;

/// <summary>A drop slot (slot type 4). Height is arg * 16 source pixels; direction is read from the trigger cell.</summary>
public sealed record Drop(int Slot, int Height);
