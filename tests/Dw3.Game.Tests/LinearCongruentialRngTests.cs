using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests;

public sealed class LinearCongruentialRngTests
{
    [Fact]
    public void Seed_zero_follows_the_rand_recurrence()
    {
        var rng = new LinearCongruentialRng(0);

        int[] values = [rng.Draw(), rng.Draw(), rng.NextValue(), rng.NextValue()];

        Assert.Equal([0, 988, 1796, 1637], values);
    }

    [Fact]
    public void Same_seed_text_replays_the_same_sequence()
    {
        var first = LinearCongruentialRng.FromSeedText("42");
        var second = LinearCongruentialRng.FromSeedText("42");

        Assert.Equal(Enumerable.Range(0, 64).Select(_ => first.Draw()), Enumerable.Range(0, 64).Select(_ => second.Draw()));
    }

    [Fact]
    public void Values_stay_in_the_encounter_domain()
    {
        var rng = LinearCongruentialRng.FromSeedText(null);

        Assert.All(Enumerable.Range(0, 10_000).Select(_ => rng.NextValue()), value => Assert.InRange(value, 0, 4095));
    }

    [Fact]
    public void Unparsable_seed_text_still_yields_values()
    {
        var rng = LinearCongruentialRng.FromSeedText("not-a-number");

        Assert.InRange(rng.Draw(), 0, 4095);
    }
}
