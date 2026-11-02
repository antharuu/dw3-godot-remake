namespace Dw3Production;

/// <summary>
/// Abstracts a running extractor child process for testability.
/// The game process reads the extractor's stdout but never opens disc files itself.
/// </summary>
public interface IExtractorProcess : IDisposable
{
    /// <summary>
    /// Returns the next line from the extractor's standard output, or null at EOF.
    /// Blocking call — runs on the extraction background thread.
    /// </summary>
    string? ReadLine();

    /// <summary>
    /// Blocks until the process exits and returns its exit code.
    /// </summary>
    int WaitForExit();
}
