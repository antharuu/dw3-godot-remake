namespace Dw3Production.Diagnostics.Battle;

/// <summary>Signals that a private source screenshot required by an optional comparison is absent.</summary>
public sealed class PsxReferenceUnavailableException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="PsxReferenceUnavailableException"/> class.</summary>
    /// <param name="reason">Missing environment variable or file description.</param>
    public PsxReferenceUnavailableException(string reason)
        : base($"PSX_REFERENCE_UNAVAILABLE: {reason}")
    {
    }
}
