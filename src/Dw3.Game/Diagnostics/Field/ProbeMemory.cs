namespace Dw3Production.Diagnostics.Field;

/// <summary>Releases managed image wrappers before a probe quits the engine.</summary>
internal static class ProbeMemory
{
    /// <summary>
    /// Runs a full collection and the pending finalizers, so wrappers of engine images captured by a
    /// long probe are not finalized while the engine shuts down.
    /// </summary>
    public static void CollectBeforeQuit()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
    }
}
