namespace Dw3.Data;

/// <summary>
/// Indicates that a pack exists but its required database metadata is incomplete.
/// </summary>
public sealed class DataPackIncomplete : DataPackException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DataPackIncomplete"/> class
    /// to report a pack whose required database metadata is absent.
    /// </summary>
    /// <param name="message">Explanation of the incomplete pack, shown to the user or logged by the launcher.</param>
    /// <param name="innerException">Underlying storage failure that prevented metadata access, or <see langword="null"/> if none.</param>
    public DataPackIncomplete(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
