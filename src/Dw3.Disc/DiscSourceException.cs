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

    /// <summary>Stable error code understood by the launcher UI.</summary>
    public string ErrorCode { get; }

    /// <inheritdoc />
    public DiscSourceException(string errorCode, string message)
        : base($"{errorCode}: {message}")
    {
        ErrorCode = errorCode;
    }

    /// <inheritdoc />
    public DiscSourceException(string errorCode, string message, Exception inner)
        : base($"{errorCode}: {message}", inner)
    {
        ErrorCode = errorCode;
    }
}
