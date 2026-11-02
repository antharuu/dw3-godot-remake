namespace Dw3Production.Diagnostics.Field;

/// <summary>Assertion used by the diagnostic probes.</summary>
internal static class ProbeRequirement
{
    /// <summary>Fails the running probe when <paramref name="condition"/> does not hold.</summary>
    /// <param name="condition">Expectation that must be true.</param>
    /// <param name="message">Observed values, printed after the <c>INPUT_PROBE_FAIL</c> prefix that the probe logs carry.</param>
    /// <exception cref="InvalidOperationException"><paramref name="condition"/> is false.</exception>
    public static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"INPUT_PROBE_FAIL: {message}");
        }
    }
}
