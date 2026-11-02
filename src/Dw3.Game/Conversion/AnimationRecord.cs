namespace Dw3Production.Conversion;

/// <summary>One animation frame record from the player manifest: frame index, source-tick duration,
/// draw offset relative to the anchor, and the foot ground-contact point.</summary>
public sealed record AnimationRecord(int Frame, int Duration, int OffsetX, int OffsetY, int FootGroundX, int FootGroundY);
