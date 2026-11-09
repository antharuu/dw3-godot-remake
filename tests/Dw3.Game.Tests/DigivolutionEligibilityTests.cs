using Dw3Production.Session;
using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests;

/// <summary>Verifies that normal Digivolution lists forms already equipped by the active partner.</summary>
public sealed class DigivolutionEligibilityTests
{
    /// <summary>Includes the base form and acquired slots without treating learning conditions as selection gates.</summary>
    [Fact]
    public void AvailableForms_IncludesBaseAndKnownFormsWithoutReevaluatingLearningRequirements()
    {
        var seed = new PartnerSeed { Id = 1 };
        var partner = new PlayerSessionPartner(seed, [2, 1, 3, 2], [], []);

        IReadOnlyList<int> forms = DigivolutionEligibility.AvailableForms(partner);

        Assert.Equal([1, 2, 3], forms);
    }
}
