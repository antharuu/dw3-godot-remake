namespace Dw3Production.Conversion;

/// <summary>One named MFIGHT template, with the source role and its TextWindow substitutions.</summary>
public sealed record BattleTextTemplate(int Entry, string Role, IReadOnlyList<string> Substitutions, string Source);
