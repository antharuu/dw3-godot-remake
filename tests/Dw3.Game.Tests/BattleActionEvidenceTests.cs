using Dw3Production.Diagnostics.Battle;
using Xunit;

namespace Dw3.Game.Tests;

public sealed class BattleActionEvidenceTests
{
    [Fact]
    public void MenuOnlyNavigationIsNotAnAction()
    {
        Assert.False(BattleActionEvidence.IsConsumed(100, 100));
    }

    [Fact]
    public void SpentTurnIsAnActionEvenWhenDamageMisses()
    {
        Assert.True(BattleActionEvidence.IsConsumed(100, 140));
    }
}
