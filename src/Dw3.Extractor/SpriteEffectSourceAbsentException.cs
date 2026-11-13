namespace Dw3.Extractor;

/// <summary>Signals a bounded zero archive offset, never an unrecognized or malformed source.</summary>
internal sealed class SpriteEffectSourceAbsentException : FormatException
{
    /// <summary>Initializes a new instance of the <see cref="SpriteEffectSourceAbsentException"/> class.</summary>
    /// <param name="message">Exact empty-entry qualification.</param>
    internal SpriteEffectSourceAbsentException(string message)
        : base(message)
    {
    }
}
