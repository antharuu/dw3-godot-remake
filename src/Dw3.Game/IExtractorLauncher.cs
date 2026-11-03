namespace Dw3Production;

/// <summary>
/// Launches an extractor child process given a disc path and output directory.
/// </summary>
public interface IExtractorLauncher
{
    /// <summary>
    /// Starts the extractor for <paramref name="discPath"/> writing output to
    /// <paramref name="outputDirectory"/>. The caller owns and must dispose the returned process.
    /// </summary>
    /// <param name="discPath">Path to the disc cue sheet passed to the extractor as a command-line argument.</param>
    /// <param name="outputDirectory">Directory where the extractor writes the SQLite and PNG data pack.</param>
    /// <returns>A running extractor process; the caller is responsible for disposal.</returns>
    IExtractorProcess Launch(string discPath, string outputDirectory);
}
