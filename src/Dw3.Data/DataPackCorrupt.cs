namespace Dw3.Data;

/// <summary>
/// Indicates that a pack failed a database or file-integrity validation.
/// </summary>
public sealed class DataPackCorrupt : DataPackException
{
    /// <summary>
    /// Initializes a corrupt-pack failure.
    /// </summary>
    /// <param name="message">Explanation of the validation failure.</param>
    /// <param name="innerException">Underlying storage failure, when available.</param>
    public DataPackCorrupt(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
