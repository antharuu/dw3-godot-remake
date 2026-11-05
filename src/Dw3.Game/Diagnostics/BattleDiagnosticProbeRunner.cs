namespace Dw3Production.Diagnostics;

/// <summary>Selects battle diagnostics in their established startup precedence.</summary>
public sealed class BattleDiagnosticProbeRunner(IDiagnosticProbeHost host)
{
    private readonly IDiagnosticProbeHost _host = host;

    /// <summary>Starts the first battle probe enabled in the options; every battle probe owns the input.</summary>
    /// <param name="options">Startup options read from the environment.</param>
    /// <returns><see langword="true"/> when a probe was started.</returns>
    public bool TryDispatch(HostStartupOptions options) =>
        TrySchedule(options.BattleCameraProbe, "RunBattleCameraProbe")
        || TrySchedule(options.BattleHudProbe, "RunBattleHudProbe")
        || TrySchedule(options.BattleInputProbe, "RunBattleInputProbe")
        || TrySchedule(options.BattlePlayProbe, "RunBattlePlayProbe")
        || TrySchedule(options.BattleMotionProbe, "RunBattleMotionProbe")
        || TrySchedule(options.BattleActionProbe, "RunBattleActionProbe")
        || TrySchedule(options.BattleFontProbe, "RunBattleFontProbe")
        || TrySchedule(options.BattleFaceProbe, "RunBattleFaceProbe")
        || TrySchedule(options.BattleModelProbe, "RunBattleModelProbe")
        || TrySchedule(options.BattleEntranceProbe, "RunBattleEntranceProbe")
        || TrySchedule(options.BattleKotemonLightProbe, "RunBattleKotemonLightProbe")
        || TrySchedule(options.BattleLightProbe, "RunBattleLightProbe")
        || TrySchedule(options.BattleReportProbe, "RunBattleReportProbe")
        || TrySchedule(options.FighterSheet, "RunFighterSheet");

    /// <summary>
    /// Schedules <paramref name="methodName"/> when <paramref name="enabled"/> is true;
    /// returns <see langword="false"/> immediately otherwise.
    /// </summary>
    /// <param name="enabled">Whether this probe is enabled by the current startup options.</param>
    /// <param name="methodName">Entry-point method name registered in the probe catalog.</param>
    /// <returns><see langword="true"/> when the probe was scheduled.</returns>
    private bool TrySchedule(bool enabled, string methodName)
    {
        if (!enabled)
        {
            return false;
        }

        _host.ScheduleProbe(methodName, ownsInput: true);
        return true;
    }
}
