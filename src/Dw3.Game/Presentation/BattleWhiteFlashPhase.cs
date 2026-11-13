namespace Dw3Production.Presentation;

/// <summary>The lifecycle of one source white flash.</summary>
internal enum BattleWhiteFlashPhase
{
    /// <summary>Brightening towards full white.</summary>
    Rising,

    /// <summary>Held at full white until ended.</summary>
    Holding,

    /// <summary>Fading back after the end command.</summary>
    Falling,

    /// <summary>Faded out and removed.</summary>
    Ended,
}
