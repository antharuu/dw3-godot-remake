using Dw3Production.Conversion;
using Dw3Production.Simulation;
using NumericsVector2 = System.Numerics.Vector2;

namespace Dw3Production.Scene;

/// <summary>
/// Builds field zones on demand and keeps every zone built during the session. Only the first
/// zone is built at startup; the others are built the first time a warp or a probe needs them,
/// so a session never decodes a zone twice.
/// </summary>
internal sealed class FieldZoneCache
{
    private readonly Dictionary<string, FieldZone> _zones = [];
    private readonly FieldDataPackLoader _loader;
    private readonly IReadOnlyList<CollisionActor> _collision;

    /// <summary>Initializes a new instance of the <see cref="FieldZoneCache"/> class.</summary>
    /// <param name="loader">Field reader of the data pack, owned by the caller.</param>
    /// <param name="collision">Collision boxes of the field actors of every zone; each zone keeps its own.</param>
    public FieldZoneCache(FieldDataPackLoader loader, IReadOnlyList<CollisionActor> collision)
    {
        _loader = loader;
        _collision = collision;
    }

    /// <summary>Gets the number of zones built so far, which is also the number kept.</summary>
    public int Count => _zones.Count;

    /// <summary>Gets the number of zone decodes performed; it equals <see cref="Count"/> since no zone is decoded twice.</summary>
    public int DecodeCount { get; private set; }

    /// <summary>Returns a zone, building it on first use.</summary>
    /// <param name="zoneId">Normalized zone id (<c>Snnn</c>).</param>
    /// <returns>The built zone.</returns>
    /// <exception cref="CacheResourceException">The zone is not in the pack or one of its map images is unusable.</exception>
    public FieldZone GetOrLoad(string zoneId)
    {
        if (_zones.TryGetValue(zoneId, out var existing))
        {
            return existing;
        }

        var zone = Build(_loader.LoadZone(zoneId));
        _zones.Add(zoneId, zone);
        DecodeCount++;
        return zone;
    }

    /// <summary>Combines a zone contract with its map planes and the actors that block movement in it.</summary>
    /// <param name="zone">Zone contract read from the pack.</param>
    /// <returns>The field zone.</returns>
    private FieldZone Build(Zone zone)
    {
        var occupants = _collision.Where(actor => actor.ZoneId == zone.Id)
            .Select(actor => new Occupant(actor.ActorId, new NumericsVector2(actor.X, actor.Y), actor.Width)).ToArray();
        return new FieldZone(zone, ZoneMapImages.LoadFloor(zone), ZoneMapImages.LoadTriggers(zone), occupants, ZoneMapImages.LoadAreas(zone));
    }
}
