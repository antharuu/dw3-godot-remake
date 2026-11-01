namespace Dw3.Extractor;

/// <summary>How a character animation sequence terminates on disc.</summary>
public enum NpcSequenceTerminator
{
    /// <summary>A final zero word repeats the sequence.</summary>
    Loop,

    /// <summary>A final minus-one word stops the sequence.</summary>
    End,
}
