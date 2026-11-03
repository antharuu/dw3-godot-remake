namespace Dw3.Data;

/// <summary>
/// Indicates that a pack failed a database or file-integrity validation.
/// </summary>
public sealed class DataPackCorruptException : DataPackException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DataPackCorruptException"/> class
    /// to report a pack that failed a database or file-integrity validation.
    /// </summary>
    /// <param name="message">Explanation of the validation failure, shown to the user or logged by the launcher.</param>
    /// <param name="innerException">Underlying storage failure that triggered the corruption check, or <see langword="null"/> if none.</param>
    public DataPackCorruptException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
