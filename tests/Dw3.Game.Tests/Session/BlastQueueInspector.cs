using System.Reflection;
using Dw3Production.Simulation;

namespace Dw3.Game.Tests.Session;

internal static class BlastQueueInspector
{
    public static BattleTurnScheduler Scheduler(BattleFlowController flow) => (BattleTurnScheduler)typeof(BattleState)
        .GetField("_turnScheduler", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(flow.State)!;

    public static IReadOnlyList<BattleScheduledEvent> Events(BattleFlowController flow)
    {
        var events = (BattleScheduledEvent?[])typeof(BattleTurnScheduler).GetField("_events", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(Scheduler(flow))!;
        return [.. events.OfType<BattleScheduledEvent>()];
    }
}
