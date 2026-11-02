using System.Diagnostics;

namespace Dw3Production;

/// <summary>
/// Wraps a live <see cref="Process"/> stdout as an <see cref="IExtractorProcess"/>.
/// </summary>
internal sealed class SystemExtractorProcess : IExtractorProcess
{
    private readonly Process _process;

    internal SystemExtractorProcess(Process process) => _process = process;

    /// <inheritdoc/>
    public string? ReadLine() => _process.StandardOutput.ReadLine();

    /// <inheritdoc/>
    public int WaitForExit()
    {
        _process.WaitForExit();
        return _process.ExitCode;
    }

    /// <inheritdoc/>
    public void Dispose() => _process.Dispose();
}
