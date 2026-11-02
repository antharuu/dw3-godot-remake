namespace Dw3Production;

/// <summary>
/// Execution states of the first-launch extraction flow.
/// </summary>
public enum ExtractionState
{
    /// <summary>Extraction has not been started.</summary>
    Idle,
    /// <summary>The extractor child process is running.</summary>
    Extracting,
    /// <summary>The extractor reported success and the pack is ready.</summary>
    Complete,
    /// <summary>The extractor reported a failure or exited with a non-zero code.</summary>
    Error,
}
