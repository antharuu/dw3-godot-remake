namespace Dw3Production.Conversion;

/// <summary>The talk-box panel: a 12x62 texture stretched over the box, semi-transparent at its
/// STP border texels. R/G/B is the dominant opaque body colour.</summary>
public sealed record PanelSpec(int R, int G, int B, bool Semi, int Tpage, int Clut, string Path, int W, int H);
