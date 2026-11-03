using Dw3Production.Conversion;

namespace Dw3Production.Simulation;

/// <summary>
/// Groups the six callback delegates required by <see cref="WarpController.Tick"/>. Packing
/// them avoids an eight-parameter call site and makes the warp transition pipeline explicit:
/// LoadTarget reads the cache, ApplyScene commits the scene change, Info/Warn are the log
/// sinks, StartClimb dispatches a wall traverse, StartDrop dispatches a cliff drop.
/// </summary>
/// <param name="LoadTarget">Loads and returns the <see cref="FieldZone"/> for the given zone id from the cache.</param>
/// <param name="ApplyScene">Commits the loaded zone as the current scene.</param>
/// <param name="Info">Logs an informational line (warp state transitions).</param>
/// <param name="Warn">Logs a warning; first argument is the key, second is the message.</param>
/// <param name="StartClimb">Dispatches a climb-up or climb-down slot (slot types 2 and 3) with its trigger direction.</param>
/// <param name="StartDrop">Dispatches a drop slot (slot type 4) with its trigger direction; null when drops are not supported.</param>
public sealed record WarpCallbacks(
    Func<string, FieldZone> LoadTarget,
    Action<FieldZone> ApplyScene,
    Action<string> Info,
    Action<string, string> Warn,
    Action<Climb, int> StartClimb,
    Action<Drop, int>? StartDrop = null);
