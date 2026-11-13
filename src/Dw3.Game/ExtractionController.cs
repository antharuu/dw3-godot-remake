using System.Text.Json;

namespace Dw3Production;

/// <summary>
/// Pure C# state machine that drives the first-launch extraction flow.
/// Parses JSONL progress lines produced by the extractor and fires events that the
/// Godot-side <c>FirstLaunchScreen</c> marshals back to the main thread.
/// </summary>
public sealed class ExtractionController
{
    private readonly IExtractorLauncher _launcher;
    private readonly Func<string, bool> _isPackUsable;

    private ExtractionState _state = ExtractionState.Idle;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExtractionController"/> class
    /// with the launcher that will spawn the extractor.
    /// </summary>
    /// <param name="launcher">Launcher used to start the extractor child process.</param>
    /// <param name="isPackUsable">Optional pack validation override for isolated tests.</param>
    public ExtractionController(IExtractorLauncher launcher, Func<string, bool>? isPackUsable = null)
    {
        ArgumentNullException.ThrowIfNull(launcher);
        _launcher = launcher;
        _isPackUsable = isPackUsable ?? Startup.DataPackValidator.IsUsable;
    }

    /// <summary>
    /// Fired when a domain step completes: (domainName, done, totalDomains).
    /// </summary>
    public event Action<string, int, int>? ProgressUpdated;

    /// <summary>Fired once when all domains complete successfully.</summary>
    public event Action? Completed;

    /// <summary>Fired with a player-actionable message when extraction fails.</summary>
    public event Action<string>? Failed;

    /// <summary>Gets the current extraction state.</summary>
    public ExtractionState State => _state;

    /// <summary>
    /// Runs extraction synchronously; intended to be called from a background thread.
    /// Events fire on the calling thread — the Godot side must marshal them to the main thread.
    /// </summary>
    /// <param name="discPath">
    /// Path to the user's disc cue sheet, passed to the extractor as an argument.
    /// This method never opens the file itself.
    /// </param>
    /// <param name="outputDirectory">Directory where the extractor writes the data pack.</param>
    public void Extract(string discPath, string outputDirectory)
    {
        if (_state == ExtractionState.Extracting)
        {
            throw new InvalidOperationException("Extraction is already in progress.");
        }

        _state = ExtractionState.Extracting;
        bool completionSeen = false;

        IExtractorProcess process;
        try
        {
            process = _launcher.Launch(discPath, outputDirectory);
        }
        catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception or IOException)
        {
            _state = ExtractionState.Error;
            Failed?.Invoke(error.Message);
            return;
        }

        using var ownedProcess = process;
        string? line;
        while ((line = process.ReadLine()) is not null)
        {
            ProcessLine(line, ref completionSeen);

            // Stop reading if the error handler transitioned us out of Extracting.
            if (_state == ExtractionState.Error)
            {
                process.WaitForExit();
                return;
            }
        }

        int exitCode = process.WaitForExit();
        if (completionSeen && exitCode == 0)
        {
            if (!_isPackUsable(outputDirectory))
            {
                _state = ExtractionState.Error;
                Failed?.Invoke("Extracted data pack is missing, incomplete, corrupt, or incompatible.");
                return;
            }

            _state = ExtractionState.Complete;
            Completed?.Invoke();
            return;
        }

        // The extractor ended without a completion event.
        _state = ExtractionState.Error;
        string reason = exitCode == 0
            ? "Extraction ended without a completion event."
            : $"Extractor exited with code {exitCode}.";
        Failed?.Invoke(reason);
    }

    /// <summary>
    /// Parses one JSONL line from the extractor's stdout and fires the appropriate event.
    /// Lines with a <c>step</c> property advance the progress counter; lines with an <c>event</c>
    /// property signal completion or failure. Non-JSON lines (diagnostic output) are ignored.
    /// </summary>
    /// <param name="json">Raw text line read from the extractor process stdout.</param>
    /// <param name="completionSeen">Whether the extractor has emitted its completion event.</param>
    private void ProcessLine(string json, ref bool completionSeen)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;

            // Progress line: {"step":"zones","done":1,"total":20}
            if (root.TryGetProperty("step", out JsonElement stepProp))
            {
                string domain = stepProp.GetString() ?? string.Empty;
                int done = root.GetProperty("done").GetInt32();
                int total = root.GetProperty("total").GetInt32();
                ProgressUpdated?.Invoke(domain, done, total);
                return;
            }

            // Event line: {"event":"complete",...} or {"event":"error","message":"..."}
            if (root.TryGetProperty("event", out JsonElement evProp))
            {
                string ev = evProp.GetString() ?? string.Empty;
                if (string.Equals(ev, "complete", StringComparison.Ordinal))
                {
                    completionSeen = true;
                }
                else if (string.Equals(ev, "error", StringComparison.Ordinal))
                {
                    string msg = root.TryGetProperty("message", out JsonElement msgProp)
                        ? msgProp.GetString() ?? "Unknown error."
                        : "Unknown error.";
                    _state = ExtractionState.Error;
                    Failed?.Invoke(msg);
                }
            }
        }
        catch (JsonException)
        {
            // Non-JSON diagnostic output is silently skipped.
        }
    }
}
