using Dw3Production.Simulation;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>
/// Drives the running game through the production <see cref="InteractiveFrameCoordinator"/> and its
/// <see cref="SimClock"/>, as Godot's <c>_Process</c> does, while a probe supplies the rendered-frame deltas. Only the
/// probe's own gates differ: the scripted-driver flag is reported off and the window is reported focused, so the same
/// fixed 60 Hz steps and the single final presentation run. It keeps a ledger of fed time and emitted ticks.
/// </summary>
internal sealed class BattleProductionFrameDriver : IInteractiveFrameHost
{
    private readonly IInteractiveFrameHost _host;
    private readonly InteractiveFrameCoordinator _frames;

    /// <summary>Initializes a new instance of the <see cref="BattleProductionFrameDriver"/> class.</summary>
    /// <param name="host">Running game's interactive-frame host.</param>
    public BattleProductionFrameDriver(IInteractiveFrameHost host)
    {
        _host = host;
        _frames = new InteractiveFrameCoordinator(new SimClock());
    }

    /// <summary>Gets the simulation ticks emitted so far.</summary>
    public int Ticks { get; private set; }

    /// <summary>Gets the rendered-frame time fed so far, in seconds.</summary>
    public double Seconds { get; private set; }

    /// <summary>Gets the rendered frames fed so far.</summary>
    public int Frames { get; private set; }

    /// <summary>Gets the ticks the clock dropped after a stall.</summary>
    public int DroppedTicks { get; private set; }

    /// <inheritdoc/>
    public bool IsTestDriverActive => false;

    /// <inheritdoc/>
    public bool HasInputFocus => true;

    /// <inheritdoc/>
    public bool IsInTitle => _host.IsInTitle;

    /// <inheritdoc/>
    public bool HasField => _host.HasField;

    /// <inheritdoc/>
    public bool HasActiveBattle => _host.HasActiveBattle;

    /// <summary>Runs one rendered frame of the given duration through the production coordinator.</summary>
    /// <param name="delta">Rendered-frame duration in seconds.</param>
    public void Advance(double delta)
    {
        Frames++;
        Seconds += delta;
        _frames.Advance(this, delta);
    }

    /// <inheritdoc/>
    public void ShowTitle() => _host.ShowTitle();

    /// <inheritdoc/>
    public void StartDebugBattleIfRequested()
    {
        // A probe never starts a battle from held keys.
    }

    /// <inheritdoc/>
    public void LogSimulationStall(int droppedTicks, int cap)
    {
        DroppedTicks += droppedTicks;
        _host.LogSimulationStall(droppedTicks, cap);
    }

    /// <inheritdoc/>
    public void SimulateStep()
    {
        Ticks++;
        _host.SimulateStep();
    }

    /// <inheritdoc/>
    public void PresentBattle(double delta) => _host.PresentBattle(delta);

    /// <inheritdoc/>
    public void PresentField(double delta) => _host.PresentField(delta);
}
