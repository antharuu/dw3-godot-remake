namespace Dw3.Data;

/// <summary>
/// Indicates that a pack exists but its required database metadata is incomplete.
/// </summary>
public sealed class DataPackIncomplete : DataPackException
{
    /// <summary>
    /// Initializes an incomplete-pack failure.
    /// </summary>
    /// <param name="message">Explanation of the incomplete pack.</param>
    /// <param name="innerException">Underlying storage failure, when available.</param>
    public DataPackIncomplete(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
