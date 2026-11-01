namespace Dw3.Formats.Field;

/// <summary>A stage actor pointer list: its own address and the null-terminated actor-entry addresses it holds.</summary>
public sealed record NpcStageActorList(uint Address, IReadOnlyList<uint> Actors);
