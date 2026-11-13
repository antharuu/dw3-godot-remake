using Dw3Production.Simulation;
using Godot;

namespace Dw3Production.Input;

/// <summary>Tracks native held controls but drops menu edges produced while a battle owns noninteractive playback.</summary>
internal static class BattleInputGate
{
    /// <summary>Forwards native state to the adapter and rejects commands that cannot belong to the current phase.</summary>
    /// <param name="adapter">Host input adapter.</param>
    /// <param name="input">Native event.</param>
    /// <param name="flow">Active battle, or null while exploring.</param>
    /// <param name="hasFightReport">Whether confirmation belongs to the post-battle report instead.</param>
    public static void Handle(InputAdapter adapter, InputEvent input, BattleFlowController? flow, bool hasFightReport)
    {
        adapter.Handle(input);
        if (flow is not null && flow.Phase != BattleFlowPhase.PlayerCommand && !hasFightReport)
        {
            if (flow.ActionResult?.Reveal.Arrow != true)
            {
                adapter.State.ConsumeActionPressed();
            }

            adapter.State.ConsumeCancelPressed();
            adapter.State.ConsumePreviousPagePressed();
            adapter.State.ConsumeNextPagePressed();
        }
    }
}
