using Dw3Production.Conversion;
using Dw3Production.Session;
using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests.Session;

/// <summary>
/// Verifies that PlayerSession.CreateDebug initializes the base form's inherent techniques through the production
/// session path, so a fresh debug session can actually use the Technique command.
/// </summary>
/// <remarks>
/// The boundary: at exact published 2b7, CreateDebug leaves every PartnerFormProgress empty and KnownTechniques=[].
/// BattleSessionTechniques.Resolve then finds no matching technique and returns an empty menu. The fix seeds the
/// base form with every skill whose SkillLevels threshold is at or below the initial form level (1), leaving
/// higher-threshold skills absent until their level is reached via normal form-EXP progression.
/// </remarks>
public sealed class DebugSessionTechniqueTests
{
    /// <summary>Skill id present in FormProgressFixture.Techniques; threshold = 1 (inherent at form level 1).</summary>
    private const int InherentTechnique = 11;

    /// <summary>Skill id present in FormProgressFixture.Techniques; threshold = 5 (not met at form level 1).</summary>
    private const int ThresholdTechnique = 12;

    /// <summary>
    /// A fresh debug session's active base form must expose its inherent technique in the Technique menu.
    /// </summary>
    [Fact]
    public void CreateDebug_BaseFormHasInherentTechniqueInMenu()
    {
        FighterData fighters = StarterCatalogue();
        PlayerSession session = PlayerSession.CreateDebug(fighters, RewardSessionFixture.Items());
        BattleFlowController flow = FormProgressFixture.Build(session, fighters);

        int baseFormId = fighters.DigimonOrdered[0].Id;
        Assert.Contains(flow.PlayerTechniquesByForm[baseFormId], t => t.Id == InherentTechnique);
    }

    /// <summary>
    /// A skill above the form's initial level threshold must be absent from the technique menu even after
    /// CreateDebug initializes the inherent skills.
    /// </summary>
    [Fact]
    public void CreateDebug_HigherThresholdTechniqueAbsentAtStart()
    {
        FighterData fighters = StarterCatalogue();
        PlayerSession session = PlayerSession.CreateDebug(fighters, RewardSessionFixture.Items());
        BattleFlowController flow = FormProgressFixture.Build(session, fighters);

        int baseFormId = fighters.DigimonOrdered[0].Id;
        Assert.DoesNotContain(flow.PlayerTechniquesByForm[baseFormId], t => t.Id == ThresholdTechnique);
    }

    /// <summary>
    /// After switching the active partner the second debug starter must also offer its own inherent technique.
    /// </summary>
    [Fact]
    public void CreateDebug_SwitchingActiveStarterPreservesOtherPartnerTechnique()
    {
        FighterData fighters = StarterCatalogue();
        PlayerSession session = PlayerSession.CreateDebug(fighters, RewardSessionFixture.Items());
        BattleFlowController flow = FormProgressFixture.Build(session, fighters);
        FormProgressFixture.Switch(flow, 1);

        int secondFormId = fighters.DigimonOrdered[6].Id;
        Assert.Contains(flow.PlayerTechniquesByForm[secondFormId], t => t.Id == InherentTechnique);
    }

    /// <summary>
    /// A fabricated catalogue with eight partner entries: three starters (positions 0, 6, 7) with an inherent
    /// technique (SkillLevels[0]=1) and a threshold technique (SkillLevels[1]=5), plus five fillers.
    /// Uses FormProgressFixture.Techniques() which already includes ids 11 and 12.
    /// </summary>
    private static FighterData StarterCatalogue()
    {
        // Eight ordered entries required; StarterPartnerDigimonIds picks positions [0, 6, 7].
        DigimonData[] ordered =
        [
            StarterForm(200), // position 0
            FillerForm(201),  // position 1
            FillerForm(202),  // position 2
            FillerForm(203),  // position 3
            FillerForm(204),  // position 4
            FillerForm(205),  // position 5
            StarterForm(206), // position 6
            StarterForm(207), // position 7
        ];

        // Enemy fighter 100 required by RewardSessionFixture.Encounters() encounter index 1.
        var battleTable = new BattleTableEntry(
            100,
            0,
            1023,
            100,
            [1, 0, 0],
            [1, 1, 1, 1, 1],
            new int[12],
            0,
            [new BattleAction(1, 0, 0)],
            new BattleAction(0, 0, 0));

        return new FighterData(
            ordered.ToDictionary(d => d.Id),
            ordered,
            [battleTable]);
    }

    /// <summary>Fabricated starter: skills[0]=basic(1), skills[1]=inherent(11), skills[2]=threshold(12).
    /// SkillLevels: inherent at form level 1, threshold at form level 5.</summary>
    private static DigimonData StarterForm(int id) => new(
        id,
        [10, 10, 10, 10, 100, 5],
        new int[7],
        [1, InherentTechnique, ThresholdTechnique],
        0,
        new int[5],
        [1, 5],
        [3, 10],
        10,
        0,
        10,
        100,
        10,
        5,
        1,
        new int[6],
        new int[7],
        [],
        id,
        0,
        false);

    /// <summary>Fabricated filler: only a basic attack, no learnable technique.</summary>
    private static DigimonData FillerForm(int id) => new(
        id,
        [10, 10, 10, 10, 100, 5],
        new int[7],
        [1],
        0,
        new int[5],
        [],
        [],
        10,
        0,
        10,
        100,
        10,
        5,
        1,
        new int[6],
        new int[7],
        [],
        id,
        0,
        false);
}
