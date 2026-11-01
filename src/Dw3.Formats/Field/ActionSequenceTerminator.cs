namespace Dw3.Formats.Field;

/// <summary>How a sequence ends on disc.</summary>
public enum ActionSequenceTerminator
{
    /// <summary>The final zero word repeats the sequence.</summary>
    Loop,

    /// <summary>The final minus-one word stops the sequence.</summary>
    End,
}
