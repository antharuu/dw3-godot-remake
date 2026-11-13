namespace Dw3Production;

/// <summary>Routes pair-confirmation cursor actions without adding host-state branching.</summary>
public static class BattlePairSwitchConfirmation
{
    /// <summary>Executes the selected ordinary switch or pair action when the cursor points at a known row.</summary>
    /// <param name="cursor">Current pair-menu row.</param>
    /// <param name="partner">Selected reserve slot.</param>
    /// <param name="switchAction">Ordinary switch simulation callback.</param>
    /// <param name="pairAction">Pair simulation callback.</param>
    /// <param name="switchAccepted">Host callback after an ordinary switch succeeds.</param>
    /// <param name="pairAccepted">Host callback after a pair action succeeds.</param>
    /// <param name="refused">Host callback when the selected action cannot mutate battle state.</param>
    public static void Confirm(
        int cursor,
        int partner,
        Func<bool> switchAction,
        Func<bool> pairAction,
        Action switchAccepted,
        Action pairAccepted,
        Action refused)
    {
        if (partner < 0)
        {
            return;
        }

        if (cursor == 0)
        {
            Complete(switchAction, switchAccepted, refused);
        }
        else if (cursor == 1)
        {
            Complete(pairAction, pairAccepted, refused);
        }
    }

    /// <summary>Runs one mutable simulation callback and dispatches its visible host outcome.</summary>
    /// <param name="action">Simulation mutation attempted from the visible row.</param>
    /// <param name="accepted">Host callback after success.</param>
    /// <param name="refused">Host callback after refusal.</param>
    private static void Complete(Func<bool> action, Action accepted, Action refused)
    {
        if (action())
        {
            accepted();
            return;
        }

        refused();
    }
}
