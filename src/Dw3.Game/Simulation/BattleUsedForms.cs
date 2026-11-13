namespace Dw3Production.Simulation;

/// <summary>Retains permanent forms that performed a legal action for each of the three party slots.</summary>
internal sealed class BattleUsedForms
{
    private readonly HashSet<int>[] _bySlot = [[], [], []];

    /// <summary>Gets the used permanent-form ids by party slot.</summary>
    public IReadOnlySet<int>[] BySlot => _bySlot;

    /// <summary>Records a permanent form when it acted from a valid party slot.</summary>
    /// <param name="slot">Party slot that acted.</param>
    /// <param name="fighter">Current fighter in that slot.</param>
    public void Mark(int slot, BattleFighter? fighter)
    {
        if (slot >= 0 && slot < _bySlot.Length && fighter is { Id: > 0, Temporary: false })
        {
            _bySlot[slot].Add(fighter.Id);
        }
    }
}
