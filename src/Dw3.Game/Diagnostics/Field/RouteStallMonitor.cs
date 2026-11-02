using NumericsVector2 = System.Numerics.Vector2;

namespace Dw3Production.Diagnostics.Field;

/// <summary>
/// Detects a scripted walk that oscillates in place. Every 150 ticks the position is compared with
/// the previous snapshot; less than half a world unit of progress means the route is stuck. A plain
/// distance-to-target check would misfire on detours that move away from the target for a while.
/// </summary>
internal sealed class RouteStallMonitor
{
    /// <summary>Ticks between two position snapshots.</summary>
    private const int SnapshotInterval = 150;

    /// <summary>Minimum progress in world units expected between two snapshots.</summary>
    private const float SnapshotMinAdvance = 0.5f;

    /// <summary>Position at the last snapshot.</summary>
    private NumericsVector2 _snapshotPosition;

    /// <summary>Initializes a new instance of the <see cref="RouteStallMonitor"/> class.</summary>
    /// <param name="start">Position at tick 0.</param>
    public RouteStallMonitor(NumericsVector2 start)
    {
        _snapshotPosition = start;
    }

    /// <summary>Gets the tick of the last snapshot.</summary>
    public int SnapshotTick { get; private set; }

    /// <summary>Checks progress on snapshot ticks and takes a new snapshot when the walk advanced.</summary>
    /// <param name="tick">Zero-based walking tick.</param>
    /// <param name="position">Current player position.</param>
    /// <returns><see langword="true"/> when the walk made too little progress since the last snapshot.</returns>
    public bool HasStalled(int tick, NumericsVector2 position)
    {
        if (tick == 0 || tick % SnapshotInterval != 0)
        {
            return false;
        }

        if (NumericsVector2.Distance(position, _snapshotPosition) < SnapshotMinAdvance)
        {
            return true;
        }

        _snapshotPosition = position;
        SnapshotTick = tick;
        return false;
    }
}
