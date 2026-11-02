namespace Dw3Production.Conversion;

/// <summary>
/// Optional PS1 stream modulate color for the depth-11 background layer.
/// Source: FIELDSTG_spriteDepths[2]=11 always uses spriteColor in the field-layer record contract (i==2).
/// Neutral is null (maps to Color(1,1,1) = no modulation).
/// </summary>
public sealed record StreamModulate(float R, float G, float B);
