using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests;

/// <summary>Closes the result message a damaging action shows once its script ended.</summary>
internal static class ActionResultTestSteps
{
    /// <summary>Requires a shown result message and ticks without input until the action completes.</summary>
    /// <param name="flow">Battle flow inside its action.</param>
    /// <param name="tick">One production tick.</param>
    public static void CloseResult(BattleFlowController flow, Action tick)
    {
        Assert.NotNull(flow.ActionResult);
        for (int frame = 0; frame < 32 && flow.Phase == BattleFlowPhase.ActionSequence; frame++)
        {
            tick();
        }

        Assert.Null(flow.ActionResult);
    }
}
