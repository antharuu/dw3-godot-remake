namespace Dw3.Formats.Field;

/// <summary>Evaluates the legacy opening-snapshot visibility contract from numeric disc conditions.</summary>
public static class NpcSnapshotEvaluator
{
    /// <summary>Returns true when all conditions in the actor's condition list pass for the given progress value.</summary>
    public static bool IsVisible(
        IReadOnlyList<NpcCondition> conditions,
        int progress,
        IReadOnlyList<NpcProgressRange> progressRanges,
        IReadOnlyDictionary<int, int> specialProgressRanges)
    {
        foreach (NpcCondition condition in conditions)
        {
            if (!EvaluateCondition(condition, progress, progressRanges, specialProgressRanges))
            {
                return false;
            }
        }

        return true;
    }

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
