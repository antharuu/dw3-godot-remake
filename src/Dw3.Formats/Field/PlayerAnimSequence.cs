namespace Dw3.Formats.Field;

/// <summary>A decoded animation sequence.</summary>
public sealed class PlayerAnimSequence
{
    /// <summary>Gets frame records in play order.</summary>
    public IReadOnlyList<PlayerAnimRecord> Records { get; }

    /// <summary>Gets type of terminator word that ends the sequence.</summary>
    public PlayerAnimSequenceTerminator Terminator { get; }

    internal PlayerAnimSequence(IReadOnlyList<PlayerAnimRecord> records, PlayerAnimSequenceTerminator terminator)
    {
        Records = records;
        Terminator = terminator;
    }
}
