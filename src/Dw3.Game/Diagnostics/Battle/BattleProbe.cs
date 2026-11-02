using Godot;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>One scripted battle diagnostic with consistent process exit and failure reporting.</summary>
internal abstract class BattleProbe
{
    /// <summary>Initializes a new instance of the <see cref="BattleProbe"/> class.</summary>
    /// <param name="host">Restricted live battle host.</param>
    protected BattleProbe(IBattleProbeHost host)
    {
        Host = host;
    }

    /// <summary>Gets the restricted live battle host.</summary>
    protected IBattleProbeHost Host { get; }

    /// <summary>Gets the stable error prefix consumed by headless probe scripts.</summary>
    protected abstract string FailureCode { get; }

    /// <summary>Runs the probe and terminates the engine with its verdict.</summary>
    /// <returns>A task that completes once termination is requested.</returns>
    public async Task RunAsync()
    {
        try
        {
            await ExecuteAsync();
            Host.Quit(0);
        }
        catch (PsxReferenceUnavailableException error)
        {
            GD.Print(error.Message);
            Host.Quit(5);
        }
        catch (Exception error)
        {
            GD.PushError($"{FailureCode}_FAIL: {error.Message}");
            Host.Quit(3);
        }
    }

    /// <summary>Drives the active battle and validates the probe-specific invariant.</summary>
    /// <returns>A task that completes after every assertion passes.</returns>
    /// <exception cref="InvalidOperationException">A required resource or assertion is unavailable.</exception>
    protected abstract Task ExecuteAsync();
}
