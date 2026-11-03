namespace Dw3.Disc;

/// <summary>
/// Raised when the user-supplied disc source cannot be read, parsed, or validated.
/// <see cref="ErrorCode"/> is a stable, player-facing identifier shown by the launcher UI.
/// </summary>
public sealed class DiscSourceException : Exception
{
    /// <summary>The disc image cannot be parsed as a supported BIN/CUE Mode-2 ISO9660 image.</summary>
    public const string FormatInvalid = "SOURCE_FORMAT_INVALID";

    /// <summary>The disc image does not match any supported game edition.</summary>
    public const string EditionInvalid = "SOURCE_EDITION_INVALID";

    /// <summary>A required file is absent or corrupt on the disc.</summary>
    public const string DiscIncomplete = "SOURCE_INCOMPLETE";

    /// <summary>
    /// Initializes a new instance of the <see cref="DiscSourceException"/> class with an error code and message.
    /// The error code must be one of the stable constants defined on this class.
    /// </summary>
    /// <param name="errorCode">Stable player-facing identifier for the failure kind (e.g. <see cref="FormatInvalid"/>).</param>
    /// <param name="message">Human-readable explanation of the specific failure.</param>
    public DiscSourceException(string errorCode, string message)
        : base($"{errorCode}: {message}")
    {
        ErrorCode = errorCode;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="DiscSourceException"/> class with an error code,
    /// message, and the underlying exception that caused this failure.
    /// </summary>
    /// <param name="errorCode">Stable player-facing identifier for the failure kind (e.g. <see cref="FormatInvalid"/>).</param>
    /// <param name="message">Human-readable explanation of the specific failure.</param>
    /// <param name="inner">Inner exception that describes the low-level cause.</param>
    public DiscSourceException(string errorCode, string message, Exception inner)
        : base($"{errorCode}: {message}", inner)
    {
        ErrorCode = errorCode;
    }

    /// <summary>Gets stable error code understood by the launcher UI.</summary>
    public string ErrorCode { get; }
}
