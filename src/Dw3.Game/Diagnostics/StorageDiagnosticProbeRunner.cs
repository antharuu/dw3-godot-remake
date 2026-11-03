namespace Dw3Production.Diagnostics;

/// <summary>Selects diagnostics for runtime storage integration.</summary>
public sealed class StorageDiagnosticProbeRunner(IDiagnosticProbeHost host)
{
    private readonly IDiagnosticProbeHost _host = host;

    /// <summary>Starts the SQLite round-trip probe when enabled.</summary>
    /// <param name="options">Startup options read from the environment.</param>
    /// <returns><see langword="true"/> when the probe was started.</returns>
    public bool TryDispatch(HostStartupOptions options)
    {
        if (!options.SqliteProbe)
        {
            return false;
        }

        _host.ScheduleProbe("RunSqliteProbe", ownsInput: true);
        return true;
    }
}
