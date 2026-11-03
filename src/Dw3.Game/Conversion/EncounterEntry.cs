namespace Dw3Production.Conversion;

/// <summary>One entry of the encounter table. <see cref="EnemyIndices"/> indexes the flat
/// enemy list; <see cref="Fighters"/> is the resolved fighter id per slot (0 = empty).</summary>
public sealed record EncounterEntry(int Index, IReadOnlyList<int> EnemyIndices, IReadOnlyList<int> Fighters, int AmbushChance, int Kind, IReadOnlyList<int> Blocks);
