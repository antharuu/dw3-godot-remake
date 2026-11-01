namespace Dw3.Formats.Battle;

/// <summary>
/// Texture state set by command <c>0x01</c>: texture page and CLUT position used by the
/// following polygons.
/// </summary>
/// <param name="UBase">Little-endian word of command bytes 1..2; its low byte offsets every U.</param>
/// <param name="Page">Texture page (command byte 2).</param>
/// <param name="VBase">Offset added to every V (command byte 3).</param>
/// <param name="ClutX">CLUT column (command byte 4).</param>
/// <param name="ClutY">CLUT row (command byte 5).</param>
/// <param name="TpageMode">Texture colour mode: 0 = 4-bit, 1 = 8-bit, 2 = 15-bit direct (command byte 6).</param>
public sealed record MeshTextureState(int UBase, int Page, int VBase, int ClutX, int ClutY, int TpageMode);
