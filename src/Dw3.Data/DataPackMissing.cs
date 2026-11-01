namespace Dw3.Data;

/// <summary>
/// Indicates that no extracted data pack exists in the configured data directory.
/// </summary>
public sealed class DataPackMissing : DataPackException
{
    /// <summary>
    /// Initializes a missing-pack failure.
    /// </summary>
    /// <param name="message">Explanation of the missing pack.</param>
    public DataPackMissing(string message)
        : base(message)
    {
    }
}
