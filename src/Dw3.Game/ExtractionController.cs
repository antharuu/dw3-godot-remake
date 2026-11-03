using System.Text.Json;

namespace Dw3Production;

/// <summary>
/// Pure C# state machine that drives the first-launch extraction flow.
/// Parses JSONL progress lines produced by the extractor and fires events that the
/// Godot-side <c>FirstLaunchScreen</c> marshals back to the main thread.
/// </summary>
public sealed class ExtractionController
{
    private ExtractionState _state = ExtractionState.Idle;
    private readonly IExtractorLauncher _launcher;

    /// <summary>Gets the current extraction state.</summary>
    public ExtractionState State => _state;

    /// <summary>
    /// Fired when a domain step completes: (domainName, done, totalDomains).
    /// </summary>
    public event Action<string, int, int>? ProgressUpdated;

    /// <summary>Fired once when all domains complete successfully.</summary>
    public event Action? Completed;

    /// <summary>Fired with a player-actionable message when extraction fails.</summary>
    public event Action<string>? Failed;

    /// <summary>
    /// Initializes a new instance of the <see cref="ExtractionController"/> class
    /// with the launcher that will spawn the extractor.
    /// </summary>
    /// <param name="launcher">Launcher used to start the extractor child process.</param>
    public ExtractionController(IExtractorLauncher launcher)
    {
        ArgumentNullException.ThrowIfNull(launcher);
        _launcher = launcher;
    }

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

        using var process = _launcher.Launch(discPath, outputDirectory);
        string? line;
        while ((line = process.ReadLine()) is not null)
        {
            ProcessLine(line);

            // Stop reading if the error handler transitioned us out of Extracting.
            if (_state == ExtractionState.Error)
            {
                process.WaitForExit();
                return;
            }
        }

        int exitCode = process.WaitForExit();
        if (_state == ExtractionState.Complete)
        {
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
    private void ProcessLine(string json)
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
                    _state = ExtractionState.Complete;
                    Completed?.Invoke();
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
