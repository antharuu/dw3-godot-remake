namespace Dw3Production.Diagnostics;

/// <summary>
/// Narrow boundary used by diagnostic dispatchers. Probe runners choose one diagnostic mode while
/// the Godot host remains responsible only for invoking its already-initialized probe entry point.
/// </summary>
public interface IDiagnosticProbeHost
{
    /// <summary>
    /// Runs the input mapping checks on a private input adapter, then ends the session with code 0.
    /// The checks need no field scene, so they run at once instead of after the first frame.
    /// </summary>
    void RunInputProbeAndQuit();

    /// <summary>Starts a registered probe after the current frame.</summary>
    /// <param name="methodName">Entry name of the probe, such as <c>RunWarpProbe</c>.</param>
    /// <param name="ownsInput">
    /// <see langword="true"/> when the probe injects all input itself; engine input events are then
    /// ignored so stray events cannot disturb its scripted route.
    /// </param>
    void ScheduleProbe(string methodName, bool ownsInput);
}
