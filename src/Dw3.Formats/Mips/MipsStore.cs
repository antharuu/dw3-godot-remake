namespace Dw3.Formats.Mips;

/// <summary>A word store whose target address and value were both materialized.</summary>
/// <param name="Offset">Byte offset of the <c>sw</c> in the analysed buffer.</param>
/// <param name="Address">Absolute target address (base register plus displacement).</param>
/// <param name="Value">The stored value.</param>
public readonly record struct MipsStore(int Offset, uint Address, MipsValue Value);
