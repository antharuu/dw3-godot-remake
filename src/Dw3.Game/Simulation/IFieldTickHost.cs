namespace Dw3Production.Simulation;

/// <summary>Exposes field simulation modes and mode-specific operations to the fixed-tick coordinator.</summary>
public interface IFieldTickHost
{
    /// <summary>Gets a value indicating whether the terminal title mode is active.</summary>
    bool IsInTitle { get; }

    /// <summary>Gets a value indicating whether collision and scene data are available.</summary>
    bool HasField { get; }

    /// <summary>Gets a value indicating whether battle mode is active.</summary>
    bool HasActiveBattle { get; }

    /// <summary>Gets a value indicating whether dialogue mode is active.</summary>
    bool HasActiveDialogue { get; }

    /// <summary>Gets a value indicating whether climb or drop traversal is active.</summary>
    bool HasActiveTraversal { get; }

    /// <summary>Advances battle mode and consumes the field diagnostic edge.</summary>
    void TickBattleMode();

    /// <summary>Advances the active dialogue state machine.</summary>
    void TickDialogueMode();

    /// <summary>Advances climb or drop traversal and recovers its landing position.</summary>
    void TickTraversalMode();

    /// <summary>Processes dialogue interaction and normal locomotion.</summary>
    /// <param name="onMovementRejection">Receives rejected movement details.</param>
    void TickLocomotionMode(Action<MovementResult> onMovementRejection);

    /// <summary>Advances warp offers and commits.</summary>
    void TickWarp();

    /// <summary>Advances field animation, icon and renderer simulation state.</summary>
    void AdvanceFieldPresentation();
}
