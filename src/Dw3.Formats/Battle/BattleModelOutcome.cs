namespace Dw3.Formats.Battle;

/// <summary>Result of decoding one fighter-table entry: either a resolved model or a failure reason.</summary>
public sealed record BattleModelOutcome(FighterModelEntry Entry, FighterModelReference? Reference, string? Reason)
{
    /// <summary>A fighter whose model decoded.</summary>
    /// <param name="reference">The resolved fighter model reference.</param>
    /// <returns>A successful outcome with the decoded reference and a derived fighter entry.</returns>
    public static BattleModelOutcome Decoded(FighterModelReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return new(new FighterModelEntry((short)reference.FighterId, (byte)reference.Index, (byte)reference.Kind), reference, null);
    }

    /// <summary>A fighter whose resolution or model decoding failed with <paramref name="reason"/>.</summary>
    /// <param name="entry">The raw fighter table entry that could not be resolved or decoded.</param>
    /// <param name="reason">Human-readable description of the failure.</param>
    /// <returns>A failed outcome with no reference and the given reason string.</returns>
    public static BattleModelOutcome Failed(FighterModelEntry entry, string reason) => new(entry, null, reason);

    /// <summary>Gets a value indicating whether true when the model decoded.</summary>
    public bool IsDecoded => Reason is null;
}
