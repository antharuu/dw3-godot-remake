namespace Dw3.QualityRatchet;

/// <summary>
/// Raised when an input of the ratchet (solution, SARIF log, Cobertura report, baseline, arguments)
/// is missing or cannot be read.
/// </summary>
public sealed class QualityInputException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="QualityInputException"/> class.
    /// </summary>
    public QualityInputException()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="QualityInputException"/> class.
    /// </summary>
    /// <param name="message">What is wrong with the input and how to produce it.</param>
    public QualityInputException(string message)
        : base(message)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="QualityInputException"/> class.
    /// </summary>
    /// <param name="message">What is wrong with the input and how to produce it.</param>
    /// <param name="innerException">The parsing or I/O error that revealed the problem.</param>
    public QualityInputException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
