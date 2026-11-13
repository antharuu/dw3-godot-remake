using Dw3Production.Session;
using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests.Session;

/// <summary>
/// Verifies that BattleSessionTechniques.Resolve gates technique menus by explicit acquisition rather than offering
/// all techniques when the partner-wide technique list is empty.
/// </summary>
/// <remarks>
/// The global (partner-wide) KnownTechniques list is populated by an external lab marking service not yet modelled
/// here. A form's locally acquired techniques (formTechniques) are populated by that form's own level progression.
/// An empty global list must not act as an open whitelist: only signature (temporary-form exception) and
/// explicitly listed techniques may appear.
/// </remarks>
public sealed class BattleSessionCatalogueTests
{
    /// <summary>
    /// An empty partner-wide technique list must not act as an open whitelist: unacquired skills must be absent
    /// from every form menu.
    /// </summary>
    [Fact]
    public void CatalogueBuild_EmptyGlobalList_UnearnedSkillNotOfferedOnAnyForm()
    {
        PlayerSession session = FormProgressFixture.Session(level: 25, knownTechniques: []);
        BattleFlowController flow = FormProgressFixture.Build(session);

        Assert.DoesNotContain(flow.PlayerTechniquesByForm[5], t => t.Id == FormProgressFixture.AcquiredTechnique);
        Assert.DoesNotContain(flow.PlayerTechniquesByForm[6], t => t.Id == FormProgressFixture.AcquiredTechnique);
    }

    /// <summary>
    /// After form 5 reaches its acquisition threshold the acquired skill appears only on form 5's menu;
    /// form 6 cannot offer it and the partner-wide list remains empty.
    /// </summary>
    [Fact]
    public void CatalogueBuild_AfterFormAcquisition_SkillOfferedOnItsFormOnlyGlobalListEmpty()
    {
        // Simulate the post-battle state: form 5 advanced to level 2, acquiring skill-entry 0 (AcquiredTechnique).
        // No global-list change is expected; the partner-wide list stays empty.
        PlayerSession session = FormProgressFixture.Session(level: 25, known: [5, 6], equipped: [5, 6], knownTechniques: []);
        PartnerFormProgress five = Partner(session, 0).FormProgress(5);
        five.Level = 2;
        five.AcquireSkill(0, FormProgressFixture.AcquiredTechnique);

        BattleFlowController flow = FormProgressFixture.Build(session);

        Assert.Contains(flow.PlayerTechniquesByForm[5], t => t.Id == FormProgressFixture.AcquiredTechnique);
        Assert.DoesNotContain(flow.PlayerTechniquesByForm[6], t => t.Id == FormProgressFixture.AcquiredTechnique);
        Assert.Empty(Partner(session, 0).KnownTechniques);
    }

    /// <summary>
    /// After form 5 reaches the mastery threshold the skill is flagged mastered on that form entry only;
    /// the partner-wide list must remain empty and form 6 must still not receive the skill.
    /// </summary>
    [Fact]
    public void CatalogueBuild_AfterMastery_SkillRemainsFormLocalNotCrossForm()
    {
        // Simulate the post-mastery state: form 5 at level 5, skill-entry 0 acquired and mastered.
        PlayerSession session = FormProgressFixture.Session(level: 25, known: [5, 6], equipped: [5, 6], knownTechniques: []);
        PartnerFormProgress five = Partner(session, 0).FormProgress(5);
        five.Level = 5;
        five.AcquireSkill(0, FormProgressFixture.AcquiredTechnique);
        five.MasterSkill(0);

        // Mastery is stored on the form entry; KnownTechniques must remain empty.
        Assert.NotEmpty(Partner(session, 0).FormProgress(5).MasteredTechniques);
        Assert.Empty(Partner(session, 0).KnownTechniques);

        BattleFlowController flow = FormProgressFixture.Build(session);

        Assert.Contains(flow.PlayerTechniquesByForm[5], t => t.Id == FormProgressFixture.AcquiredTechnique);
        Assert.DoesNotContain(flow.PlayerTechniquesByForm[6], t => t.Id == FormProgressFixture.AcquiredTechnique);
    }

    /// <summary>
    /// When the partner-wide list contains a skill that skill is offered on every form regardless of local
    /// acquisition, confirming the global-list path still works after the shortcut is removed.
    /// </summary>
    [Fact]
    public void CatalogueBuild_NonEmptyGlobalList_GlobalSkillOfferedOnAllForms()
    {
        PlayerSession session = FormProgressFixture.Session(level: 25, knownTechniques: [FormProgressFixture.AcquiredTechnique]);
        BattleFlowController flow = FormProgressFixture.Build(session);

        Assert.Contains(flow.PlayerTechniquesByForm[5], t => t.Id == FormProgressFixture.AcquiredTechnique);
        Assert.Contains(flow.PlayerTechniquesByForm[6], t => t.Id == FormProgressFixture.AcquiredTechnique);
    }

    /// <summary>Returns one session partner.</summary>
    private static PlayerSessionPartner Partner(PlayerSession session, int index) =>
        Assert.IsType<PlayerSessionPartner>(session.Partners[index]);
}
