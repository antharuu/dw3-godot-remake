namespace Dw3.Formats.Battle;

/// <summary>One fixed-size item record from the boot executable.</summary>
public sealed record ItemRecord(uint DataPointer, ushort Price, ushort SellPrice, byte Kind, byte Type);
