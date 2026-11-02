namespace Dw3Production.Simulation;

/// <summary>Supplies frame-state decisions and presentation operations to the interactive-frame coordinator.</summary>
public interface IInteractiveFrameHost
{
    /// <summary>Gets a value indicating whether a scripted diagnostic driver owns input and frames.</summary>
    bool IsTestDriverActive { get; }

    /// <summary>Gets a value indicating whether application input currently has focus.</summary>
    bool HasInputFocus { get; }

    /// <summary>Gets a value indicating whether the terminal title screen is active.</summary>
    bool IsInTitle { get; }

    /// <summary>Gets a value indicating whether an initialized field is available for simulation.</summary>
    bool HasField { get; }

    /// <summary>Gets a value indicating whether simulation currently has an active battle.</summary>
    bool HasActiveBattle { get; }

    /// <summary>Keeps the terminal title screen visible for the rendered frame.</summary>
    void ShowTitle();

    /// <summary>Consumes the F8 edge and starts the debug battle when it is present.</summary>
    void StartDebugBattleIfRequested();

    /// <summary>Records a fixed-step stall after the clock drops catch-up ticks.</summary>
    /// <param name="droppedTicks">Number of dropped simulation ticks.</param>
    /// <param name="cap">Maximum ticks allowed in one rendered frame.</param>
    void LogSimulationStall(int droppedTicks, int cap);

    /// <summary>Advances one fixed simulation tick.</summary>
    void SimulateStep();

    /// <summary>Presents the final battle state for the rendered frame.</summary>
    void PresentBattle();

    /// <summary>Presents the final field state for the rendered frame.</summary>
    /// <param name="delta">Elapsed rendered-frame time.</param>
    void PresentField(double delta);
}
