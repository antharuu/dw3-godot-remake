using Dw3Production.Conversion;
using Dw3Production.Session;
using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests.Session;

/// <summary>
/// Tests the form experience share, the form level curve and the separate skill acquisition and mastery states
/// against a ledger derived by hand from the original report rules, with fabricated forms only.
/// </summary>
public sealed class PartnerFormProgressionTests
{
    /// <summary>Checks the share against the hand ledger, including the level-50 branch, the minimum and both caps.</summary>
    /// <param name="reward">Encounter form experience.</param>
    /// <param name="partnerLevel">Partner level before its own experience.</param>
    /// <param name="used">Used slots over the fought party.</param>
    /// <param name="formLevel">Form level before the grant.</param>
    /// <param name="expected">Ledger share.</param>
    [Theory]
    [InlineData(30, 25, 1, 1, 10)] // 300 / 25 = 12, capped at 10 below ExpLevel 10
    [InlineData(30, 25, 2, 1, 7)] // 12 * 6 / 10 = 7
    [InlineData(30, 25, 3, 1, 4)] // 12 / 3 = 4
    [InlineData(30, 25, 4, 1, 3)] // 12 / 4 = 3
    [InlineData(30, 60, 1, 1, 6)] // past level 50: 30 / 5 = 6
    [InlineData(30, 50, 1, 1, 6)] // level 50 still uses 300 / 50 = 6
    [InlineData(1, 25, 3, 1, 1)] // 10 / 25 = 0, raised to the minimum 1
    [InlineData(0, 25, 1, 1, 1)] // a used form always receives at least 1
    [InlineData(300, 10, 1, 9, 10)] // 300, form below ExpLevel: cap 10
    [InlineData(300, 10, 1, 10, 50)] // form at ExpLevel: cap 50
    [InlineData(30, 25, 1, 10, 12)] // form at ExpLevel keeps 12 under the cap 50
    public void Share_FollowsTheLedger(int reward, int partnerLevel, int used, int formLevel, int expected) =>
        Assert.Equal(expected, FormExpRules.Share(new FormExpAward(reward, partnerLevel, used), formLevel, 10));

    /// <summary>Checks the curve: 10 a level up to ExpLevel, 50 a level after it, and no growth past 99.</summary>
    [Fact]
    public void AddExp_RaisesThroughSeveralLevelsAndStopsAt99()
    {
        Assert.Equal((10, 90, 140, 190), (FormExpRules.RequiredExp(1, 10), FormExpRules.RequiredExp(9, 10), FormExpRules.RequiredExp(10, 10), FormExpRules.RequiredExp(11, 10)));
        var progress = new PartnerFormProgress(5);

        Assert.True(FormExpRules.AddExp(progress, 35, 10));
        Assert.Equal((4, 35), (progress.Level, progress.Exp));
        Assert.False(FormExpRules.AddExp(progress, 4, 10));
        Assert.Equal((4, 39), (progress.Level, progress.Exp));

        var top = new PartnerFormProgress(5, 99, 9_999_990);
        Assert.False(FormExpRules.AddExp(top, 50, 10));
        Assert.Equal((99, 9_999_999), (top.Level, top.Exp));
    }

    /// <summary>
    /// Acquisition at level 2 makes the skill usable by its form only, mastery waits for level 5, a battle
    /// without a level-up checks neither, and nothing is learned twice.
    /// </summary>
    [Fact]
    public void Apply_SeparatesAcquisitionFromLaterMastery()
    {
        PlayerSessionPartner partner = FormProgressFixture.Partner(1, 25, null, null);
        FighterData fighters = FormProgressFixture.Fighters(skillLevels: [2, 3], knownLevels: [5, 99]);
        var award = new FormExpAward(30, 25, 1);

        FightReportFormSlot first = Single(partner, award, fighters);
        Assert.Equal((0, 5, 0, 10, 1, 2), (first.Slot, first.DigimonId, first.ExpBefore, first.ExpGained, first.LevelBefore, first.LevelAfter));
        Assert.Equal([FormProgressFixture.AcquiredTechnique], first.AcquiredTechniques);
        Assert.Empty(first.MasteredTechniques);
        Assert.Equal([FormProgressFixture.AcquiredTechnique], partner.FormTechniques(5));
        Assert.Empty(partner.FormTechniques(6));
        Assert.Empty(partner.FormProgress(5).MasteredTechniques);
        Assert.Equal([1, 2], partner.KnownTechniques);

        Assert.Equal([FormProgressFixture.LateTechnique], Single(partner, award, fighters).AcquiredTechniques);
        FightReportFormSlot quiet = Single(partner, new FormExpAward(1, 25, 1), fighters);
        Assert.Equal((20, 1, 3, 3), (quiet.ExpBefore, quiet.ExpGained, quiet.LevelBefore, quiet.LevelAfter));
        Assert.Empty(quiet.AcquiredTechniques);
        Assert.Empty(Single(partner, award, fighters).MasteredTechniques);

        FightReportFormSlot mastery = Single(partner, award, fighters);
        Assert.Equal((4, 5), (mastery.LevelBefore, mastery.LevelAfter));
        Assert.Empty(mastery.AcquiredTechniques);
        Assert.Equal([FormProgressFixture.AcquiredTechnique], mastery.MasteredTechniques);
        Assert.True(partner.FormProgress(5).IsMastered(0));
        Assert.False(partner.FormProgress(5).IsMastered(1));

        FightReportFormSlot after = Single(partner, award, fighters);
        Assert.Empty(after.AcquiredTechniques);
        Assert.Empty(after.MasteredTechniques);
        Assert.Equal([FormProgressFixture.AcquiredTechnique, FormProgressFixture.LateTechnique], partner.FormProgress(5).Techniques);
        Assert.Equal([1, 2], partner.KnownTechniques);
    }

    /// <summary>Acquires all six entries at once but never masters the sixth.</summary>
    [Fact]
    public void Apply_NeverMastersTheSixthEntry()
    {
        DigimonData six = new(5, new int[6], new int[7], [1, 21, 22, 23, 24, 25, 26], 0, [], [1, 1, 1, 1, 1, 1], [1, 1, 1, 1, 1], 10, 0, 0, 0, 0, 0, 0, new int[6], new int[7], [], 5, 0, false);
        var fighters = new FighterData(new Dictionary<int, DigimonData> { [5] = six }, [six], []);
        PlayerSessionPartner partner = FormProgressFixture.Partner(1, 25, null, null);

        FightReportFormSlot row = Single(partner, new FormExpAward(30, 25, 1), fighters);

        Assert.Equal([21, 22, 23, 24, 25, 26], row.AcquiredTechniques);
        Assert.Equal([21, 22, 23, 24, 25], row.MasteredTechniques);
        Assert.False(partner.FormProgress(5).IsMastered(5));
    }

    /// <summary>Refuses a used slot that holds no equipped form instead of inventing a base-form row.</summary>
    [Fact]
    public void Apply_RejectsContradictoryUsedSlot()
    {
        PlayerSessionPartner partner = FormProgressFixture.Partner(1, 25, [5], [5]);

        Assert.Throws<InvalidOperationException>(() => PartnerFormProgression.Apply(partner, [1], new FormExpAward(30, 25, 1), FormProgressFixture.Fighters(), null));
    }

    /// <summary>Applies one battle where only slot zero was used.</summary>
    /// <param name="partner">Persistent partner.</param>
    /// <param name="award">Share inputs.</param>
    /// <param name="fighters">Fabricated catalogue.</param>
    /// <returns>The only row.</returns>
    private static FightReportFormSlot Single(PlayerSessionPartner partner, FormExpAward award, FighterData fighters) =>
        Assert.Single(PartnerFormProgression.Apply(partner, [0], award, fighters, null).Slots);
}
