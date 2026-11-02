namespace Dw3Production.Conversion;

/// <summary>One renderable battle text line. Tokens remain source-font tokens; Debug is only an
/// auditable description for probe logs and is never a player-facing substitute.</summary>
public sealed record BattleTextLine(IReadOnlyList<TextToken> Tokens, IReadOnlyList<IReadOnlyList<TextToken>> Substitutions, string Debug, BattleTextLine? Second = null, int Type = 1)
{
    /// <summary>Gets a sentinel line for battle text entries that could not be resolved from the data pack.</summary>
    public static readonly BattleTextLine Missing = new([], [], "[UNSOURCED battle text]");
}
