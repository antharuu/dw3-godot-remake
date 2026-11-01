namespace Dw3.Formats.Water;

/// <summary>Describes one textured rectangle in the object VRAM sprite sheet.</summary>
/// <param name="U">Texel column, which selects the 4-bit or 8-bit page.</param>
/// <param name="V">Texel row inside that page.</param>
/// <param name="Width">Rectangle width in texels.</param>
/// <param name="Height">Rectangle height in texels.</param>
/// <param name="ClutX">Palette column relative to the matching palette row.</param>
/// <param name="ClutY">Palette row encoded by the source sheet.</param>
/// <param name="Mode">Zero for 4-bit texels or one for 8-bit texels.</param>
public sealed record SpritePart(short U, short V, short Width, short Height, short ClutX, short ClutY, short Mode);
