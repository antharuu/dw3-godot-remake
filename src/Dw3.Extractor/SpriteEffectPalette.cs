namespace Dw3.Extractor;

/// <summary>The sixteen colour words the GPU reads for one sprite part, with where they come from.</summary>
/// <param name="Words">The palette exactly as read, colour word zero meaning a transparent texel.</param>
/// <param name="Source">
/// <c>effect-page</c> when the CLUT lies in the sheet's own loaded texture pages, or
/// <c>cleared-vram</c> when the packed GPU address lands in VRAM that no game mode writes after
/// its full clears, so the words are zero.
/// </param>
/// <param name="VramX">The packed GPU CLUT column, or -1 when the sheet placement is unknown.</param>
/// <param name="VramY">The packed GPU CLUT row after the nine-bit row field, or -1 when unknown.</param>
internal sealed record SpriteEffectPalette(ushort[] Words, string Source, int VramX, int VramY);
