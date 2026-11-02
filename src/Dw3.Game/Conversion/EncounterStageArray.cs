namespace Dw3Production.Conversion;

/// <summary>One stageBattles array: the source overlay RAM address and its places.</summary>
public sealed record EncounterStageArray(uint Addr, IReadOnlyList<EncounterFieldBattles> Places);
