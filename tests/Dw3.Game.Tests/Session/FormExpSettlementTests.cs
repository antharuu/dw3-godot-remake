using Dw3Production;
using Dw3Production.Session;
using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests.Session;

/// <summary>
/// Drives fabricated battles through the production settlement seam and checks the form experience share
/// against an independently derived ledger of the original report rules.
/// </summary>
/// <remarks>
/// Ledger (reward 30, partner level 25 before its own experience, form level 1, ExpLevel 10):
/// base n = 30 * 10 / 25 = 12. One used slot keeps 12, two used slots keep 12 * 6 / 10 = 7, three used
/// slots keep 12 / 3 = 4. A form below its ExpLevel is capped at 10, so the grants are 10, 7 and 4.
/// </remarks>
public sealed class FormExpSettlementTests
{
    /// <summary>Shares one encounter's form experience among the slots used across the fought party.</summary>
    /// <param name="usedSlots">Number of equipped slots the active partner fought as.</param>
    /// <param name="expected">Ledger grant per used form.</param>
    [Theory]
    [InlineData(1, 10)]
    [InlineData(2, 7)]
    [InlineData(3, 4)]
    public void WonBattle_SharesFormExpByPartnerLevelAndGlobalUsedSlots(int usedSlots, int expected)
    {
        PlayerSession session = FormProgressFixture.Session(level: 25);
        BattleFlowController flow = FormProgressFixture.Build(session);
        int[] forms = [.. FormProgressFixture.EquippedForms.Take(usedSlots)];
        FormProgressFixture.FightAs(flow, forms);

        BattleOutcomeResolution? resolution = GameHost.BattleSetupCoordinator.ResolveOutcome(session, flow, () => FormProgressFixture.Victory(digimonExp: 30));

        Assert.NotNull(resolution);
        PlayerSessionPartner partner = Assert.IsType<PlayerSessionPartner>(session.Partners[0]);
        Assert.All(forms, form => Assert.Equal(expected, partner.FormProgress(form).Exp));
    }
}
