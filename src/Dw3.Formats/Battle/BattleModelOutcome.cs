namespace Dw3.Formats.Battle;

/// <summary>Result of decoding one fighter-table entry: either a resolved model or a failure reason.</summary>
public sealed record BattleModelOutcome(FighterModelEntry Entry, FighterModelReference? Reference, string? Reason)
{
    /// <summary>A fighter whose model decoded.</summary>
    public static BattleModelOutcome Decoded(FighterModelReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);
        return new(new FighterModelEntry((short)reference.FighterId, (byte)reference.Index, (byte)reference.Kind), reference, null);
    }

    /// <summary>A fighter whose resolution or model decoding failed with <paramref name="reason"/>.</summary>
    public static BattleModelOutcome Failed(FighterModelEntry entry, string reason) => new(entry, null, reason);

    /// <summary>True when the model decoded.</summary>
    public bool IsDecoded => Reason is null;
}
