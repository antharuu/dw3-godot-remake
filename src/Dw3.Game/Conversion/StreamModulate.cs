namespace Dw3Production.Conversion;

/// <summary>
/// Optional PS1 stream modulate color for the depth-11 background layer.
/// On the disc the third field layer (depth 11) always draws with its record's sprite color.
/// Neutral is null (maps to Color(1,1,1) = no modulation).
/// </summary>
public sealed record StreamModulate(float R, float G, float B);
