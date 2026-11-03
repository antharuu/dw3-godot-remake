namespace Dw3Production.Diagnostics;

/// <summary>Selects field diagnostics in their established startup precedence.</summary>
public sealed class FieldDiagnosticProbeRunner(IDiagnosticProbeHost host)
{
    private readonly IDiagnosticProbeHost _host = host;

    /// <summary>Starts the first field probe enabled in the options, in the fixed precedence order.</summary>
    /// <param name="options">Startup options read from the environment.</param>
    /// <returns><see langword="true"/> when a probe was started; at most one runs per session.</returns>
    public bool TryDispatch(HostStartupOptions options)
    {
        if (options.InputProbe)
        {
            _host.RunInputProbeAndQuit();
            return true;
        }

        return TrySchedule(options.LocomotionProbe, "RunLocomotionProbe", ownsInput: false)
            || TrySchedule(options.WarpProbe, "RunWarpProbe", ownsInput: false)
            || TrySchedule(options.F3Probe, "RunF3Probe", ownsInput: true)
            || TrySchedule(options.F3PerfProbe, "RunF3PerformanceProbe", ownsInput: true)
            || TrySchedule(options.DepthProbe, "RunDepthProbe", ownsInput: false)
            || TrySchedule(options.WaterProbe, "RunWaterProbe", ownsInput: true)
            || TrySchedule(options.SortYProbe, "RunSortYProbe", ownsInput: true)
            || TrySchedule(options.CueProbe, "RunCueProbe", ownsInput: true)
            || TrySchedule(options.CanalWaterProbe, "RunCanalWaterProbe", ownsInput: true)
            || TrySchedule(options.M1AcceptanceTest, "RunM1AcceptanceTest", ownsInput: true)
            || TrySchedule(options.M2NavProbe, "RunM2NavProbe", ownsInput: true)
            || TrySchedule(options.ConditionalProbe, "RunConditionalProbe", ownsInput: true)
            || TrySchedule(options.GatedBridgeProbe, "RunGatedBridgeProbe", ownsInput: true)
            || TrySchedule(options.NpcProbe, "RunNpcProbe", ownsInput: true)
            || TrySchedule(options.M2VideoProbe, "RunM2VideoProbe", ownsInput: true)
            || TrySchedule(options.ObjectProbe, "RunObjectProbe", ownsInput: true)
            || TrySchedule(options.ClimbProbe, "RunClimbProbe", ownsInput: true)
            || TrySchedule(options.DropProbe, "RunDropProbe", ownsInput: true);
    }

    /// <summary>
    /// Schedules <paramref name="methodName"/> when <paramref name="enabled"/> is true;
    /// returns <see langword="false"/> immediately otherwise.
    /// </summary>
    /// <param name="enabled">Whether this probe is enabled by the current startup options.</param>
    /// <param name="methodName">Entry-point method name registered in the probe catalog.</param>
    /// <param name="ownsInput">When <see langword="true"/>, the probe takes exclusive control of keyboard input.</param>
    /// <returns><see langword="true"/> when the probe was scheduled.</returns>
    private bool TrySchedule(bool enabled, string methodName, bool ownsInput)
    {
        if (!enabled) return false;
        _host.ScheduleProbe(methodName, ownsInput);
        return true;
    }
}
