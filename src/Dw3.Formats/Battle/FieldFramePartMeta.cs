namespace Dw3.Formats.Battle;

/// <summary>Part metadata from field_frame._render_frame semantics (u/v/w/h/dx/dy/semi/clutRow).</summary>
public sealed record FieldFramePartMeta(int U, int V, int W, int H, int Dx, int Dy, bool Semi, int ClutRow);
