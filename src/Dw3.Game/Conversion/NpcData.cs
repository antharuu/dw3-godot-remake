namespace Dw3Production.Conversion;

/// <summary>All visible NPCs at the disc snapshot (progress=0x04): per-zone render
/// records plus the occupancy occupants used by the free-tile test of locomotion.</summary>
public sealed record NpcData(IReadOnlyDictionary<string, IReadOnlyList<NpcRecord>> Zones, IReadOnlyList<CollisionActor> Occupants);
