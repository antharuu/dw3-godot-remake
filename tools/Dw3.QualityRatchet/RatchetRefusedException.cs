namespace Dw3.QualityRatchet;

/// <summary>
/// Raised when the update command would raise a violation count, or when coverage is below the minimum.
/// </summary>
public sealed class RatchetRefusedException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RatchetRefusedException"/> class.
    /// </summary>
    public RatchetRefusedException()
    {
        Regressions = [];
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RatchetRefusedException"/> class.
    /// </summary>
    /// <param name="message">Why the update is refused.</param>
    public RatchetRefusedException(string message)
        : base(message)
    {
        Regressions = [];
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RatchetRefusedException"/> class.
    /// </summary>
    /// <param name="message">Why the update is refused.</param>
    /// <param name="innerException">The error that caused the refusal.</param>
    public RatchetRefusedException(string message, Exception innerException)
        : base(message, innerException)
    {
        Regressions = [];
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="RatchetRefusedException"/> class.
    /// </summary>
    /// <param name="regressions">The descriptions of the differences that the update would have written.</param>
    public RatchetRefusedException(IReadOnlyList<string> regressions)
        : base("Update refused: the baseline only goes down. Fix these regressions first.")
    {
        Regressions = regressions;
    }

    /// <summary>
    /// Gets the descriptions of the differences that the update would have written.
    /// </summary>
    public IReadOnlyList<string> Regressions { get; }
}
