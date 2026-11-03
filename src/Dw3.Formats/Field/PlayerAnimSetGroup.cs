namespace Dw3.Formats.Field;

/// <summary>One entry in the player animation-set table.</summary>
public sealed class PlayerAnimSetGroup
{
    /// <summary>Initializes a new instance of the <see cref="PlayerAnimSetGroup"/> class.</summary>
    /// <param name="setId">Animation set identifier that groups these sequences.</param>
    /// <param name="sequenceIndices">Ordered sequence indices within the parent archive.</param>
    internal PlayerAnimSetGroup(int setId, IReadOnlyList<int> sequenceIndices)
    {
        SetId = setId;
        SequenceIndices = sequenceIndices;
    }

    /// <summary>Gets animation set identifier (1=STAND, 4=WALK, 5=RUN, 6=STOP, 7=unnamed).</summary>
    public int SetId { get; }

    /// <summary>
    /// Gets five archive-entry indices (one per facing direction) pointing to
    /// animation sequences within the same offset table.
    /// </summary>
    public IReadOnlyList<int> SequenceIndices { get; }
}
