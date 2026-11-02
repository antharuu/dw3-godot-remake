using Dw3Production.Conversion;

namespace Dw3Production.Simulation;

/// <summary>Groups the active field state required to start one source-backed conversation.</summary>
public sealed class DialogueStartRequest
{
    /// <summary>Gets the decoded dialogue tables.</summary>
    public required DialogueData Data { get; init; }

    /// <summary>Gets the active field zone.</summary>
    public required FieldZone Zone { get; init; }

    /// <summary>Gets the player state used to locate the facing tile and set the standing pose.</summary>
    public required GameState Player { get; init; }

    /// <summary>Gets the dialogue state machine that receives the selected talk.</summary>
    public required DialogueController Controller { get; init; }

    /// <summary>Gets the throttled warning sink keyed by its first argument.</summary>
    public required Action<string, string> Warn { get; init; }
}
