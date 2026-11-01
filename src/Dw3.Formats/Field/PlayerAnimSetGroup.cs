namespace Dw3.Formats.Field;

/// <summary>One entry in the player animation-set table.</summary>
public sealed class PlayerAnimSetGroup
{
    /// <summary>Gets animation set identifier (1=STAND, 4=WALK, 5=RUN, 6=STOP, 7=unnamed).</summary>
    public int SetId { get; }

    /// <summary>
    /// Gets five archive-entry indices (one per facing direction) pointing to
    /// animation sequences within the same offset table.
    /// </summary>
    public IReadOnlyList<int> SequenceIndices { get; }

    internal PlayerAnimSetGroup(int setId, IReadOnlyList<int> sequenceIndices)
    {
        SetId = setId;
        SequenceIndices = sequenceIndices;
    }
}
