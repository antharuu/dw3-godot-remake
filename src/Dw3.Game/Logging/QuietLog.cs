using Godot;

namespace Dw3Production.Logging;

/// <summary>Throttled Godot log wrapper: the first occurrence of a keyed warning is emitted
/// immediately, the second adds a repeat count, and further repeats are suppressed.</summary>
public sealed class QuietLog
{
    private readonly Dictionary<string, int> _repetitions = [];

    /// <summary>Emits an informational line to the Godot output channel.</summary>
    /// <param name="message">The message text to emit.</param>
    public static void Info(string message) => GD.Print($"INFO {message}");

    /// <summary>An error-level line (the Godot error channel, for the battle-load diagnostic).</summary>
    /// <param name="message">The error message text.</param>
    public static void Error(string message) => GD.PushError($"ERROR {message}");

    /// <summary>Emits a warning the first and second time it is raised; suppresses subsequent duplicates.</summary>
    /// <param name="key">A stable key that identifies the warning category for deduplication.</param>
    /// <param name="message">The human-readable warning text.</param>
    public void Warn(string key, string message)
    {
        var count = _repetitions.GetValueOrDefault(key) + 1;
        _repetitions[key] = count;
        if (count == 1)
        {
            GD.PushWarning($"WARN {message}");
        }
        else if (count == 2)
        {
            GD.PushWarning($"WARN {message} repeats={count}");
        }
    }

    /// <summary>Gets a value indicating whether any warning keyed under the given prefix has been emitted.
    /// Lets probes assert a warning fired without coupling to GD.PushWarning output.</summary>
    /// <param name="keyPrefix">The prefix to match against recorded warning keys.</param>
    /// <returns>True when at least one matching key has been emitted.</returns>
    public bool Warned(string keyPrefix) => _repetitions.Keys.Any(key => key.StartsWith(keyPrefix, StringComparison.Ordinal));
}
