namespace Dw3Production.Presentation;

/// <summary>
/// Phase of the field exit-cue balloon.
/// The EU source advances its 8.8 frame clock by 0x133 at 50 PAL VSYNCs per second.
/// </summary>
public enum ExitCuePhase
{
    /// <summary>Balloon is not shown.</summary>
    Hidden,

    /// <summary>Balloon is animating open.</summary>
    Opening,

    /// <summary>Balloon is fully open and cycling the inner direction sprite.</summary>
    Open,

    /// <summary>Balloon is animating closed.</summary>
    Closing,
}
