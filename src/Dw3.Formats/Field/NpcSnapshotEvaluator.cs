namespace Dw3.Formats.Field;

/// <summary>Evaluates the legacy opening-snapshot visibility contract from numeric disc conditions.</summary>
public static class NpcSnapshotEvaluator
{
    /// <summary>Returns true when all conditions in the actor's condition list pass for the given progress value.</summary>
    /// <param name="conditions">The actor's condition list from the overlay.</param>
    /// <param name="progress">Current game progress value to test against.</param>
    /// <param name="progressRanges">Progress-range table from the disc executable.</param>
    /// <param name="specialProgressRanges">Special condition id to range index mapping.</param>
    /// <returns>True when all conditions pass; false when any condition fails.</returns>
    public static bool IsVisible(
        IReadOnlyList<NpcCondition> conditions,
        int progress,
        IReadOnlyList<NpcProgressRange> progressRanges,
        IReadOnlyDictionary<int, int> specialProgressRanges) =>
        conditions.All(condition => EvaluateCondition(condition, progress, progressRanges, specialProgressRanges));

    /// <summary>Returns true when a single condition passes for the given progress value.</summary>
    /// <param name="condition">Condition code and comparison operand.</param>
    /// <param name="progress">Current game progress value to test against.</param>
    /// <param name="progressRanges">Range table indexed by special-condition lookup.</param>
    /// <param name="specialProgressRanges">Special condition id to range index mapping.</param>
    /// <returns>True when the condition holds; false when it fails.</returns>
    private static bool EvaluateCondition(
        NpcCondition condition,
        int progress,
        IReadOnlyList<NpcProgressRange> progressRanges,
        IReadOnlyDictionary<int, int> specialProgressRanges)
    {
        int group = (condition.Code >> 8) & 0xFE;
        int argument = condition.Code & 0x1FF;
        bool expected = condition.Value != 0;

        if (group == 0x60)
        {
            return expected ? progress == argument : progress != argument;
        }

        if (group == 0x70)
        {
            return EvaluateRangeCondition(argument, progress, progressRanges, specialProgressRanges, expected);
        }

        return !expected;
    }

    /// <summary>Returns true when progress falls inside the special-condition range.</summary>
    /// <param name="argument">Special-condition argument used to look up the range index.</param>
    /// <param name="progress">Current game progress value.</param>
    /// <param name="progressRanges">Range table from the disc executable.</param>
    /// <param name="specialProgressRanges">Special condition id to range index mapping.</param>
    /// <param name="expected">True when the condition expects to be inside the range.</param>
    /// <returns>True when the condition holds based on the range membership and expected flag.</returns>
    private static bool EvaluateRangeCondition(
        int argument,
        int progress,
        IReadOnlyList<NpcProgressRange> progressRanges,
        IReadOnlyDictionary<int, int> specialProgressRanges,
        bool expected)
    {
        if (!specialProgressRanges.TryGetValue(argument, out int rangeIndex) || rangeIndex >= progressRanges.Count)
        {
            return false;
        }

        NpcProgressRange range = progressRanges[rangeIndex];
        bool inside = progress >= range.Minimum && progress <= range.Maximum;
        return expected ? inside : !inside;
    }
}
