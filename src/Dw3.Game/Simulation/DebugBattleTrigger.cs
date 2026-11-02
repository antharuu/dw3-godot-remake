namespace Dw3Production.Simulation;

/// <summary>Consumes the one-shot F8 debug-battle edge and routes it to the session battle starter.</summary>
public static class DebugBattleTrigger
{
    /// <summary>Starts one debug battle when the semantic F8 edge is available.</summary>
    /// <param name="input">Input state that owns the consumable F8 edge.</param>
    /// <param name="startBattle">Operation that starts the debug battle.</param>
    public static void TryStart(InputState input, Action startBattle)
    {
        if (input.ConsumeF8Pressed())
        {
            startBattle();
        }
    }
}
