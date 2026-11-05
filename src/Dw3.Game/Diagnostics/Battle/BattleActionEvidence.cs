namespace Dw3Production.Diagnostics.Battle;

/// <summary>Distinguishes a spent simulation turn from opening or cancelling a menu.</summary>
internal static class BattleActionEvidence
{
    /// <summary>Reports whether an input advanced the live player's scheduled turn.</summary>
    /// <param name="before">Scheduled player turn before the native input tick.</param>
    /// <param name="after">Scheduled player turn immediately after that tick.</param>
    /// <returns>Whether the player spent a turn, including attacks that missed.</returns>
    public static bool IsConsumed(int before, int after) => after > before;
}
