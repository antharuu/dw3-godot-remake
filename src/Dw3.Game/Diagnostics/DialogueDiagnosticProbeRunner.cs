namespace Dw3Production.Diagnostics;

/// <summary>Selects dialogue and field-overlay presentation diagnostics.</summary>
public sealed class DialogueDiagnosticProbeRunner(IDiagnosticProbeHost host)
{
    private readonly IDiagnosticProbeHost _host = host;

    /// <summary>Starts the dialogue probe, or else the connection-icon probe, when enabled.</summary>
    /// <param name="options">Startup options read from the environment.</param>
    /// <returns><see langword="true"/> when a probe was started.</returns>
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
