using Dw3Production.Logging;

namespace Dw3Production.Simulation;

/// <summary>
/// Test-time fixture that preloads the exploration story progress so that the warp
/// state machine admits gated zones during integration probes. GENJI_BEATEN is the
/// minimum progress value that opens all story-gated exits in Asuka City.
/// </summary>
public static class StoryFixture
{
    /// <summary>Story-progress constant: Genji boss beaten (0x04). Minimum value to open Asuka City gated exits.</summary>
    public const int StoryGenjiBeaten = 0x04;

    /// <summary>
    /// Sets the story progress in <paramref name="state"/> to the exploration baseline
    /// so that all gated exits in Asuka City are admitted.
    /// </summary>
    /// <param name="state">The game state to mutate.</param>
    public static void Apply(GameState state)
    {
        state.Progress = StoryGenjiBeaten;
    }

    /// <summary>
    /// Logs a warning that the given dispatch category has been suppressed for the
    /// exploration phase. Only "combat", "service" and "save" are valid categories;
    /// any other value indicates a programming error in the probe fixture.
    /// </summary>
    /// <param name="category">The dispatch category being suppressed; must be "combat", "service" or "save".</param>
    /// <param name="log">The quiet log sink that receives the suppression notice.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="category"/> is not one of the three valid values.</exception>
    public static void SuppressExclusiveDispatch(string category, QuietLog log)
    {
        if (category is not ("combat" or "service" or "save"))
        {
            throw new ArgumentOutOfRangeException(nameof(category));
        }

        log.Warn("suppression", $"suppress={category}");
    }
}
