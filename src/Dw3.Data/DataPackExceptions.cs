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

/// <summary>
/// Indicates that a pack was produced for a schema version this runtime cannot read.
/// </summary>
public sealed class DataPackSchemaMismatch : DataPackException
{
    /// <summary>
    /// Initializes a schema-version failure.
    /// </summary>
    /// <param name="message">Explanation of the incompatible version.</param>
    public DataPackSchemaMismatch(string message)
        : base(message)
    {
    }
}

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
