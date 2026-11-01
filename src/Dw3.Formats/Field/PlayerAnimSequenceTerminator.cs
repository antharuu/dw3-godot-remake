namespace Dw3.Formats.Field;

/// <summary>How an animation sequence ends.</summary>
public enum PlayerAnimSequenceTerminator
{
    /// <summary>Word 0 at end of sequence — loop back to start.</summary>
    Loop,

    /// <summary>Word -1 at end of sequence — play once and stop.</summary>
    End,
}
