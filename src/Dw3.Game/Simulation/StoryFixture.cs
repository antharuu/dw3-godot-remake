using Dw3Production.Logging;

namespace Dw3Production.Simulation;

public sealed class StoryFixture
{
    public const int StoryGenjiBeaten = 0x04;

    public void Apply(GameState state)
    {
        state.Progress = StoryGenjiBeaten;
    }

    public void SuppressExclusiveDispatch(string category, QuietLog log)
    {
        if (category is not ("combat" or "service" or "save"))
            throw new ArgumentOutOfRangeException(nameof(category));
        log.Warn("suppression", $"suppress={category}");
    }
}
