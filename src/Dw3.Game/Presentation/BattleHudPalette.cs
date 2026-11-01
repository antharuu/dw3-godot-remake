namespace Dw3Production.Presentation;

/// <summary>
/// Text palette rows of the battle HUD: each selects one CLUT row (0..7) of the decoded font atlas,
/// so text is drawn in its exact PSX colour instead of a Godot tint.
/// </summary>
internal static class BattleHudPalette
{
    /// <summary>Row 0: white, the default text colour.</summary>
    public const int White = 0;

    /// <summary>Row 2: dark blue, used by the page labels of the technique and item lists.</summary>
    public const int DarkBlue = 2;

    /// <summary>Row 7: grey, used for disabled commands and knocked-out partners.</summary>
    public const int Grey = 7;
}
