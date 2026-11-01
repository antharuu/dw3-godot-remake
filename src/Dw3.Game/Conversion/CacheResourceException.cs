namespace Dw3Production.Conversion;

/// <summary>A runtime data resource is unavailable or violates its contract. The message starts
/// with a stable code (CACHE_RESOURCE_UNAVAILABLE or CACHE_INVALID) followed by the detail.</summary>
public sealed class CacheResourceException : Exception
{
    public CacheResourceException(string code, string detail) : base($"{code}: {detail}") { }
}
