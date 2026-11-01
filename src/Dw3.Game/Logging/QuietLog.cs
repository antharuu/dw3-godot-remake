using Godot;

namespace Dw3Production.Logging;

public sealed class QuietLog
{
    private readonly Dictionary<string, int> _repetitions = [];

    public void Info(string message) => GD.Print($"INFO {message}");

    public void Warn(string key, string message)
    {
        var count = _repetitions.GetValueOrDefault(key) + 1;
        _repetitions[key] = count;
        if (count == 1) GD.PushWarning($"WARN {message}");
        else if (count == 2) GD.PushWarning($"WARN {message} repeats={count}");
    }

    /// <summary>An error-level line (the Godot error channel, for the battle-load diagnostic).</summary>
    public void Error(string message) => GD.PushError($"ERROR {message}");

    /// <summary>True once any warning keyed under the given prefix has been emitted.
    /// Lets probes assert a warning fired without coupling to GD.PushWarning output.</summary>
    public bool Warned(string keyPrefix) => _repetitions.Keys.Any(key => key.StartsWith(keyPrefix, StringComparison.Ordinal));
}
