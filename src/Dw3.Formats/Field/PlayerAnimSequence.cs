namespace Dw3.Formats.Field;

/// <summary>A decoded animation sequence.</summary>
public sealed class PlayerAnimSequence
{
    /// <summary>Gets frame records in play order.</summary>
    public IReadOnlyList<PlayerAnimRecord> Records { get; }

    /// <summary>Gets type of terminator word that ends the sequence.</summary>
    public PlayerAnimSequenceTerminator Terminator { get; }

    /// <summary>Initializes a new instance of the <see cref="PlayerAnimSequence"/> class.</summary>
    /// <param name="records">Ordered animation records that make up the sequence.</param>
    /// <param name="terminator">Terminator type word that ends the sequence in the binary.</param>
    internal PlayerAnimSequence(IReadOnlyList<PlayerAnimRecord> records, PlayerAnimSequenceTerminator terminator)
    {
        Records = records;
        Terminator = terminator;
    }
}
