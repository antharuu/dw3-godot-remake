namespace Dw3.Data;

/// <summary>
/// Base class for failures that prevent the game from using an extracted data pack.
/// </summary>
public abstract class DataPackException : Exception
{
    /// <summary>
    /// Initializes a data-pack failure with a player-actionable message.
    /// </summary>
    /// <param name="message">Explanation of the rejected pack.</param>
    /// <param name="innerException">Underlying storage failure, when available.</param>
    protected DataPackException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
