namespace Dw3Production.Presentation;

/// <summary>
/// Orders draw callbacks inside one source ordering-table depth. The PS1 layer
/// executes higher callback priorities first, then prepends their primitives,
/// so final painter order is smaller ground/sort Y first. Equal priorities keep
/// callback registration stable, then reverse at the ordering-table head: the
/// later registration draws first.
/// </summary>
public static class SourceCallbackOrder
{
    /// <summary>Stable registration rank used for field objects that predate the player callback.</summary>
    public const int MapObjectRegistration = 0;

    /// <summary>Stable registration rank used for the player callback after map-object callbacks.</summary>
    public const int PlayerRegistration = 1;

    /// <summary>Sorts callbacks into their final painter order for one source depth bucket.</summary>
    /// <typeparam name="T">Payload carried by each draw callback.</typeparam>
    /// <param name="callbacks">Callbacks with their source priority and stable registration rank.</param>
    /// <returns>Callback payloads ordered from the first primitive painted to the last.</returns>
    public static IReadOnlyList<T> Order<T>(IEnumerable<(T Item, int Priority, int Registration)> callbacks)
        => [.. callbacks
            .OrderBy(entry => entry.Priority)
            .ThenByDescending(entry => entry.Registration)
            .Select(entry => entry.Item)];
}
