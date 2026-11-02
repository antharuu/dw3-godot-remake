namespace Dw3.Data;

/// <summary>
/// Indicates that no extracted data pack exists in the configured data directory.
/// </summary>
public sealed class DataPackMissing : DataPackException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DataPackMissing"/> class
    /// to report that no extracted pack exists in the configured data directory.
    /// </summary>
    /// <param name="message">Explanation of the missing pack, shown to the user or logged by the launcher.</param>
    public DataPackMissing(string message)
        : base(message)
    {
    }
}
