namespace Dw3.Formats.Dialogue;

/// <summary>Represents an exception raised when a stage dialogue setup cannot be recovered safely.</summary>
public sealed class StageAnalysisException : FormatException
{
    /// <summary>Initializes a new instance of the <see cref="StageAnalysisException"/> class.</summary>
    /// <param name="mode">Field mode selecting the failed overlay.</param>
    /// <param name="stageId">Numeric WSTAG identifier.</param>
    /// <param name="inner">Failure raised while recovering the stage setup.</param>
    public StageAnalysisException(ushort mode, int stageId, Exception inner)
        : base($"Stage mode 0x{mode:X3} (WSTAG{stageId:D3}): {inner.Message}", inner)
    {
        Mode = mode;
        StageId = stageId;
    }

    /// <summary>Gets the field mode selecting the failed overlay.</summary>
    public ushort Mode { get; }

    /// <summary>Gets the numeric WSTAG identifier.</summary>
    public int StageId { get; }
}
