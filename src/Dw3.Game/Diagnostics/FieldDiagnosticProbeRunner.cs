namespace Dw3Production.Diagnostics;

/// <summary>
/// Narrow boundary used by diagnostic dispatchers. Probe runners choose one diagnostic mode while
/// the Godot host remains responsible only for invoking its already-initialized probe entry point.
/// </summary>
public interface IDiagnosticProbeHost
{
    void RunInputProbeAndQuit();
    void ScheduleProbe(string methodName, bool ownsInput);
}

/// <summary>Selects field diagnostics in their established startup precedence.</summary>
public sealed class FieldDiagnosticProbeRunner(IDiagnosticProbeHost host)
{
    private readonly IDiagnosticProbeHost _host = host;

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

    private bool TrySchedule(bool enabled, string methodName, bool ownsInput)
    {
        if (!enabled) return false;
        _host.ScheduleProbe(methodName, ownsInput);
        return true;
    }
}
