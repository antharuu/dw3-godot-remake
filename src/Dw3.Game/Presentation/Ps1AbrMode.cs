namespace Dw3Production.Presentation;

/// <summary>Names the PS1 semi-transparency equations that occur in the decoded fighter corpus.</summary>
public enum Ps1AbrMode
{
    /// <summary>Writes an opaque texel. The STP bit has no blend effect in this mode.</summary>
    Opaque = -1,

    /// <summary>Blends STP texels as one half background plus one half foreground.</summary>
    HalfBackgroundHalfForeground = 0,

    /// <summary>Adds STP texels to the background with per-channel saturation.</summary>
    Additive = 1,

    /// <summary>Names the source subtraction mode, which is not observed in the fighter corpus.</summary>
    UnobservedSubtract = 2,

    /// <summary>Names the source quarter-additive mode, which is not observed in the fighter corpus.</summary>
    UnobservedQuarterAdditive = 3,
}
