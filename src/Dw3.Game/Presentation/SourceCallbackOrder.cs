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
    public const int MapObjectRegistration = 0;
    public const int PlayerRegistration = 1;

    public static IReadOnlyList<T> Order<T>(IEnumerable<(T Item, int Priority, int Registration)> callbacks)
        => [.. callbacks
            .OrderBy(entry => entry.Priority)
            .ThenByDescending(entry => entry.Registration)
            .Select(entry => entry.Item)];
}
