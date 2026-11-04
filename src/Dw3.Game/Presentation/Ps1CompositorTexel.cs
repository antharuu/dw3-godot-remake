namespace Dw3Production.Presentation;

/// <summary>Stores one unmodulated source texel after palette lookup and STP decoding.</summary>
/// <param name="Red">Red component in the source eight-bit display domain.</param>
/// <param name="Green">Green component in the source eight-bit display domain.</param>
/// <param name="Blue">Blue component in the source eight-bit display domain.</param>
/// <param name="Transparent">Whether CLUT word zero suppresses this texel entirely.</param>
/// <param name="Stp">Whether this texel selects the primitive's ABR blend equation.</param>
public readonly record struct Ps1CompositorTexel(byte Red, byte Green, byte Blue, bool Transparent, bool Stp);
