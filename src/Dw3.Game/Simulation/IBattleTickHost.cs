namespace Dw3Production.Simulation;

/// <summary>Exposes battle phases and host-owned presentation operations to the fixed-tick coordinator.</summary>
public interface IBattleTickHost
{
    /// <summary>Gets the active battle flow, or null after teardown.</summary>
    BattleFlowController? Flow { get; }

    /// <summary>Gets a value indicating whether the detailed fight report is active.</summary>
    bool HasFightReport { get; }

    /// <summary>Gets a value indicating whether player-command camera setup has run.</summary>
    bool IsPlayerCameraActive { get; }

    /// <summary>Gets a value indicating whether terminal outcome effects have been applied.</summary>
    bool IsBattleResolved { get; }

    /// <summary>Advances and presents the detailed fight report.</summary>
    void TickFightReport();

    /// <summary>Activates the command-menu camera.</summary>
    void ActivatePlayerCamera();

    /// <summary>Marks the command-menu camera inactive before an enemy action.</summary>
    void DeactivatePlayerCamera();

    /// <summary>Consumes battle-menu input.</summary>
    void TickBattleMenu();

    /// <summary>Resolves one enemy action.</summary>
    void EnemyAct();

    /// <summary>Advances the active action-sequence timeline by one frame and handles its events.
    /// Called by the coordinator every tick while <see cref="BattleFlowController.Phase"/> is <c>ActionSequence</c>.</summary>
    void TickActionSequence();

    /// <summary>Refreshes models after fighter changes.</summary>
    void RefreshModels();

    /// <summary>Applies terminal reward effects once.</summary>
    void ResolveOutcome();

    /// <summary>Marks terminal reward effects as applied.</summary>
    void MarkBattleResolved();

    /// <summary>Advances the victory fade and reports whether the detailed report should start.</summary>
    /// <returns>True when the fade reached its terminal frame.</returns>
    bool AdvanceVictoryFade();

    /// <summary>Starts the detailed victory report.</summary>
    void BeginFightReport();

    /// <summary>Ends battle mode and returns to field or title.</summary>
    void EndBattle();
}
