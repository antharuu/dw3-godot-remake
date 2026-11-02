using Godot;

namespace Dw3Production.Diagnostics.Field;

/// <summary>
/// One scripted field diagnostic. A probe drives the running game through
/// <see cref="IFieldProbeHost"/>, asserts what it observes and ends the process: exit code 0 on
/// a pass, 3 with a <c>&lt;CODE&gt;_FAIL</c> engine error on the first failed assertion.
/// </summary>
internal abstract class FieldProbe
{
    /// <summary>Initializes a new instance of the <see cref="FieldProbe"/> class.</summary>
    /// <param name="host">Running game the probe drives.</param>
    protected FieldProbe(IFieldProbeHost host)
    {
        Host = host;
    }

    /// <summary>Gets the running game the probe drives.</summary>
    protected IFieldProbeHost Host { get; }

    /// <summary>Gets the failure code printed before <c>_FAIL</c>, which the probe scripts grep for.</summary>
    protected abstract string FailureCode { get; }

    /// <summary>Runs the probe to completion, then quits the engine with its verdict.</summary>
    /// <returns>A task that completes once the quit has been requested.</returns>
    public async Task RunAsync()
    {
        try
        {
            await ExecuteAsync();
            Host.Quit(0);
        }
        catch (Exception error)
        {
            GD.PushError($"{FailureCode}_FAIL: {error.Message}");
            Host.Quit(3);
        }
    }

    /// <summary>Drives the game and asserts the probe expectations.</summary>
    /// <returns>A task that completes when every assertion passed.</returns>
    /// <exception cref="InvalidOperationException">An assertion failed or a required resource was missing.</exception>
    protected abstract Task ExecuteAsync();
}
