using Dw3Production.Session;
using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests;

/// <summary>Verifies that normal Digivolution lists forms already equipped by the active partner.</summary>
public sealed class DigivolutionEligibilityTests
{
    /// <summary>Lists the base form and the explicitly equipped slots in their selected order.</summary>
    [Fact]
    public void AvailableForms_UsesExplicitEquippedSlotsRatherThanAllAcquiredForms()
    {
        var seed = new PartnerSeed { Id = 10 };
        var partner = new PlayerSessionPartner(seed, [10, 20, 30, 40, 50], [50, 20, 40], [], []);

        IReadOnlyList<int> forms = DigivolutionEligibility.AvailableForms(partner);

        Assert.Equal([10, 50, 20, 40], forms);
    }

    /// <summary>Rejects a fourth equipped normal form rather than silently truncating the source slots.</summary>
    [Fact]
    public void Constructor_RejectsMoreThanThreeEquippedForms()
    {
        var seed = new PartnerSeed { Id = 10 };

        Assert.Throws<ArgumentException>(() => new PlayerSessionPartner(seed, [10, 20, 30, 40, 50], [20, 30, 40, 50], [], []));
    }

    /// <summary>Rejects an equipped form absent from the partner's acquired form records.</summary>
    [Fact]
    public void Constructor_RejectsEquippedFormThatIsNotAcquired()
    {
        var seed = new PartnerSeed { Id = 10 };

        Assert.Throws<ArgumentException>(() => new PlayerSessionPartner(seed, [10, 20], [30], [], []));
    }
}
