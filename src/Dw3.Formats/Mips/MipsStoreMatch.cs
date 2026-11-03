namespace Dw3.Formats.Mips;

/// <summary>A store to the requested field: the <c>sw</c> byte offset and the stored value.</summary>
public readonly record struct MipsStoreMatch(int Offset, MipsValue Value);
