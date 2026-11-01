namespace Dw3.Data;

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
