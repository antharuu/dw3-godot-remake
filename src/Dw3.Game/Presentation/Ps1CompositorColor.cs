namespace Dw3Production.Presentation;

/// <summary>Stores one framebuffer colour in the compositor's eight-bit display domain.</summary>
/// <param name="Red">Red component.</param>
/// <param name="Green">Green component.</param>
/// <param name="Blue">Blue component.</param>
public readonly record struct Ps1CompositorColor(byte Red, byte Green, byte Blue);
