namespace Dw3.Data;

/// <summary>
/// Indicates that a pack was produced for a schema version this runtime cannot read.
/// </summary>
public sealed class DataPackSchemaMismatch : DataPackException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="DataPackSchemaMismatch"/> class
    /// to report that the pack was produced for an incompatible schema version.
    /// </summary>
    /// <param name="message">Explanation naming the pack's schema version and the required version.</param>
    public DataPackSchemaMismatch(string message)
        : base(message)
    {
    }
}
