namespace Dw3.Data;

/// <summary>
/// Base class for failures that prevent the game from using an extracted data pack.
/// </summary>
public abstract class DataPackException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DataPackException"/> class with a
    /// player-actionable message explaining why the data pack was rejected.
    /// </summary>
    /// <param name="message">Explanation of the rejected pack, shown to the user or logged by the launcher.</param>
    /// <param name="innerException">Underlying storage failure that triggered the rejection, or <see langword="null"/> if none.</param>
    protected DataPackException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
