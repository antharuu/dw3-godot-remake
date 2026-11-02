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
    IExtractorProcess Launch(string discPath, string outputDirectory);
}
