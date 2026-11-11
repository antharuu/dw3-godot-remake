namespace Dw3Production.Input;

/// <summary>Aggregates physical sources for one semantic button and requires release after focus loss.</summary>
internal sealed class InputButtonLatch
{
    private readonly HashSet<int> _sources = [];
    private bool _blocked;

    /// <summary>Gets a value indicating whether any physical source remains pressed.</summary>
    public bool Held => _sources.Count > 0;

    /// <summary>Preserves physical holds while disarming the semantic edge.</summary>
    public void Suspend() => _blocked = Held;

    /// <summary>Updates one physical source and reports only a newly armed aggregate press.</summary>
    /// <param name="source">Keyboard sentinel or native controller id.</param>
    /// <param name="pressed">Physical state.</param>
    /// <param name="focused">Whether an action is currently allowed.</param>
    /// <returns>True only for a fresh focused press after every source released.</returns>
    public bool Update(int source, bool pressed, bool focused)
    {
        bool wasHeld = Held;
        if (pressed)
        {
            _sources.Add(source);
            _blocked |= !focused;
            return !wasHeld && focused && !_blocked;
        }

        _sources.Remove(source);
        if (!Held)
        {
            _blocked = false;
        }

        return false;
    }
}
