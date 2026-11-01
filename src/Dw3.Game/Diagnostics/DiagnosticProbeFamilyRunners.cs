namespace Dw3Production.Diagnostics;

/// <summary>Selects battle diagnostics in their established startup precedence.</summary>
public sealed class BattleDiagnosticProbeRunner(IDiagnosticProbeHost host)
{
    private readonly IDiagnosticProbeHost _host = host;

    public bool TryDispatch(HostStartupOptions options) =>
        TrySchedule(options.BattleCameraProbe, "RunBattleCameraProbe")
        || TrySchedule(options.BattleHudProbe, "RunBattleHudProbe")
        || TrySchedule(options.BattleInputProbe, "RunBattleInputProbe")
        || TrySchedule(options.BattlePlayProbe, "RunBattlePlayProbe")
        || TrySchedule(options.BattleMotionProbe, "RunBattleMotionProbe")
        || TrySchedule(options.BattleFontProbe, "RunBattleFontProbe")
        || TrySchedule(options.BattleFaceProbe, "RunBattleFaceProbe")
        || TrySchedule(options.BattleModelProbe, "RunBattleModelProbe")
        || TrySchedule(options.BattleEntranceProbe, "RunBattleEntranceProbe")
        || TrySchedule(options.BattleKotemonLightProbe, "RunBattleKotemonLightProbe")
        || TrySchedule(options.BattleLightProbe, "RunBattleLightProbe")
        || TrySchedule(options.BattleReportProbe, "RunBattleReportProbe")
        || TrySchedule(options.FighterSheet, "RunFighterSheet");

    private bool TrySchedule(bool enabled, string methodName)
    {
        if (!enabled) return false;
        _host.ScheduleProbe(methodName, ownsInput: true);
        return true;
    }
}

/// <summary>Selects dialogue and field-overlay presentation diagnostics.</summary>
public sealed class DialogueDiagnosticProbeRunner(IDiagnosticProbeHost host)
{
    private readonly IDiagnosticProbeHost _host = host;

    public bool TryDispatch(HostStartupOptions options)
    {
        string? methodName = options.DialogueProbe ? "RunDialogueProbe"
            : options.IconProbe ? "RunIconProbe"
            : null;
        if (methodName is null) return false;
        _host.ScheduleProbe(methodName, ownsInput: true);
        return true;
    }
}

/// <summary>Selects diagnostics for runtime storage integration.</summary>
public sealed class StorageDiagnosticProbeRunner(IDiagnosticProbeHost host)
{
    private readonly IDiagnosticProbeHost _host = host;

    public bool TryDispatch(HostStartupOptions options)
    {
        if (!options.SqliteProbe) return false;
        _host.ScheduleProbe("RunSqliteProbe", ownsInput: true);
        return true;
    }
}
