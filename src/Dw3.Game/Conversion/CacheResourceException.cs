namespace Dw3Production.Conversion;

/// <summary>A runtime data resource is unavailable or violates its contract. The message starts
/// with a stable code (CACHE_RESOURCE_UNAVAILABLE or CACHE_INVALID) followed by the detail.</summary>
public sealed class CacheResourceException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CacheResourceException"/> class
    /// with the given machine-readable code and human-readable detail.
    /// </summary>
    /// <param name="code">Machine-readable error code prepended to the exception message (e.g. CACHE_RESOURCE_UNAVAILABLE).</param>
    /// <param name="detail">Human-readable description of the specific failure, appended after the code.</param>
    public CacheResourceException(string code, string detail)
        : base($"{code}: {detail}")
    {
    }
}
