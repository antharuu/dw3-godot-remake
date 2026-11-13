using Dw3Production.Conversion;
using Dw3Production.Session;
using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests.Session;

/// <summary>Tests persistent form experience, technique learning and unlocked-form delivery after victory.</summary>
public sealed class PartnerFormProgressionTests
{
    /// <summary>Delivers form experience without a level, then through multiple levels, and makes learned actions available to the next battle.</summary>
    [Fact]
    public void Apply_TracksUsedFormLevelsTechniquesAndUnlockedForms()
    {
        var partner = new PlayerSessionPartner(RewardSessionFixture.Seed(1), [1], [], [], [], RewardSessionFixture.Growth());
        FighterData fighters = Fighters();
        var tree = new PartnerDigivolution(0, 1, [new DigivolutionEntry(0, 2, [new EvolutionNeed(0, 1, 2)], 0, 0)]);

        FormProgressionResult first = PartnerFormProgression.Apply(partner, [1], 9, fighters, tree);
        Assert.Equal(1, partner.FormProgress(1).Level);
        Assert.Equal(9, partner.FormProgress(1).Exp);
        Assert.Empty(first.LearnedTechniques);
        Assert.Empty(first.LearnedForms);

        FormProgressionResult second = PartnerFormProgression.Apply(partner, [1], 21, fighters, tree);
        Assert.Equal(4, partner.FormProgress(1).Level);
        Assert.Equal(30, partner.FormProgress(1).Exp);
        Assert.Equal([11, 12], partner.FormProgress(1).Techniques);
        Assert.Contains(2, partner.KnownForms);
        Assert.Contains(2, partner.EquippedForms);
        Assert.Equal([11, 12], second.LearnedTechniques);
        Assert.Equal([2], second.LearnedForms);
    }

    /// <summary>Builds fabricated form definitions with 10 experience per level and two teachable skills.</summary>
    /// <returns>The fabricated fighter catalogue.</returns>
    private static FighterData Fighters()
    {
        DigimonData baseForm = Digimon(1, [0, 11, 12], [2, 3]);
        DigimonData unlocked = Digimon(2, [0], []);
        return new FighterData(
            new Dictionary<int, DigimonData> { [1] = baseForm, [2] = unlocked },
            [baseForm, unlocked],
            []);
    }

    /// <summary>Builds one fabricated form definition.</summary>
    /// <param name="id">Form id.</param>
    /// <param name="skills">Source skill slots.</param>
    /// <param name="skillLevels">Source levels that add the skills.</param>
    /// <returns>The fabricated form.</returns>
    private static DigimonData Digimon(int id, IReadOnlyList<int> skills, IReadOnlyList<int> skillLevels) => new(
        id,
        new int[6],
        new int[7],
        skills,
        0,
        [],
        skillLevels,
        [],
        99,
        0,
        0,
        0,
        0,
        0,
        0,
        new int[6],
        new int[7],
        [],
        id,
        0,
        false);
}
