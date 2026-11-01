namespace Dw3.Formats.Battle;

/// <summary>One rendered sprite part placed at its frame-relative offset.</summary>
/// <param name="Dx">Horizontal offset in pixels from the frame record (s16 on disc).</param>
/// <param name="Dy">Vertical offset in pixels from the frame record (s16 on disc).</param>
/// <param name="Image">Pixels of the part, sized to the part's width and height.</param>
public readonly record struct HudRenderedPart(int Dx, int Dy, HudRgbaImage Image);
