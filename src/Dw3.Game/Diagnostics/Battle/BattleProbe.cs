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

    /// <summary>Gets the exit code requested when <see cref="ExecuteAsync"/> completes without throwing.</summary>
    protected virtual int CompletedExitCode => 0;

    /// <summary>Runs the probe and terminates the engine with its verdict.</summary>
    /// <returns>A task that completes once termination is requested.</returns>
    public async Task RunAsync()
    {
        int exitCode;
        try
        {
            await ExecuteAsync();
            exitCode = CompletedExitCode;
        }
        catch (PsxReferenceUnavailableException error)
        {
            GD.Print(error.Message);
            exitCode = 5;
        }
        catch (Exception error)
        {
            GD.PushError($"{FailureCode}_FAIL: {error.Message}");
            GD.Print($"{FailureCode}_FAIL_DETAIL: {error}");
            exitCode = 3;
        }

        Host.QuitDeferred(exitCode);
    }

    /// <summary>Drives the active battle and validates the probe-specific invariant.</summary>
    /// <returns>A task that completes after every assertion passes.</returns>
    /// <exception cref="InvalidOperationException">A required resource or assertion is unavailable.</exception>
    protected abstract Task ExecuteAsync();

    /// <summary>
    /// Presses and releases a key, advancing the battle simulation one tick after each edge, so the menu
    /// sees a complete native press exactly as from a keyboard.
    /// </summary>
    /// <param name="key">Godot key to inject.</param>
    /// <param name="times">How many complete presses to inject, one after the other.</param>
    protected void InjectBattleKey(Key key, int times = 1)
    {
        for (int press = 0; press < times; press++)
        {
            Host.Input.Handle(new InputEventKey { Keycode = key, Pressed = true });
            Host.TickTestSimulation();
            Host.Input.Handle(new InputEventKey { Keycode = key, Pressed = false });
            Host.TickTestSimulation();
        }
    }
}
