using Dw3Production.Diagnostics.Field;
using Dw3Production.Simulation;
using Godot;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Drives normal battle progression for native proofs: item rows chosen from the real item page and
/// ordinary turns advanced through the host's enemy callback and root Attack input.</summary>
internal static class NativeTurnDriver
{
    /// <summary>Rows per item page, as shown by the production item menu.</summary>
    private const int ItemPageRows = 7;

    /// <summary>Opens the real item page from the root and confirms the row holding an item.</summary>
    /// <param name="session">Native host session at the player's command phase.</param>
    /// <param name="itemId">Item whose row is derived from the bag order the production opener lists.</param>
    public static void UseItem(NativeMenuSession session, int itemId)
    {
        int row = session.Flow.ItemStock.Where(entry => entry.Value > 0).Select(entry => entry.Key).ToList().IndexOf(itemId);
        ProbeRequirement.Require(row >= 0, $"item 0x{itemId:X2} is not listed by the bag");
        NativeMenuCommandsProof.Select(session, 4);
        session.Tap(Key.Enter);
        ProbeRequirement.Require(session.Menu.Menu == BattleMenuKind.Item, $"item page did not open for 0x{itemId:X2}");
        for (int page = 0; page < row / ItemPageRows; page++)
        {
            session.Tap(Key.Pagedown);
        }

        for (int index = 0; index < row % ItemPageRows; index++)
        {
            session.Tap(Key.Down);
        }

        ProbeRequirement.Require(session.Menu.First + session.Menu.Cursor == row && session.Menu.CanConfirm, $"item row {row} not selectable for 0x{itemId:X2}");
        session.Tap(Key.Enter);
    }

    /// <summary>Uses an item through the real page and returns its observed stock debit, phase and ordered draws.</summary>
    /// <param name="session">Native host session at the player's command phase.</param>
    /// <param name="itemId">Item to confirm.</param>
    /// <param name="expectedDraws">Exact ordered draws the production item consumer must take, formatted as in the proof line.</param>
    /// <returns>Proof fragment with stock, phase and the draws consumed by the item command.</returns>
    public static string UseItemObserved(NativeMenuSession session, int itemId, string expectedDraws)
    {
        int stock = session.Flow.ItemStock[itemId];
        int from = session.Rng.Count;
        UseItem(session, itemId);
        int after = session.Flow.ItemStock[itemId];
        ProbeRequirement.Require(after == stock - 1, $"item 0x{itemId:X2} stock not debited once {stock}->{after}");
        ProbeRequirement.Require(session.Flow.Phase == BattleFlowPhase.EnemyTurn, $"item 0x{itemId:X2} did not spend the turn phase={session.Flow.Phase}");
        string draws = session.Rng.DrawsSince(from);
        ProbeRequirement.Require(draws == expectedDraws, $"item 0x{itemId:X2} ordered draws {draws} differ from {expectedDraws}");
        return $"stock={stock}->{after} phase={session.Flow.Phase} rng={draws}";
    }

    /// <summary>Prints one per-case proof line.</summary>
    /// <param name="family">Mechanism family.</param>
    /// <param name="itemId">Item id.</param>
    /// <param name="before">Initial relevant state.</param>
    /// <param name="after">Resolved relevant state.</param>
    /// <param name="action">Stock, phase and ordered draws of the item command.</param>
    /// <param name="expiry">Expiry observation.</param>
    public static void Report(string family, int itemId, string before, string after, string action, string expiry) =>
        GD.Print($"NATIVE_ITEM_MATRIX PASS case={family} id=0x{itemId:X2} fixture=SYNTHETIC before={before} after={after} {action} {expiry}");

    /// <summary>Advances ordinary turns until a condition holds, using the enemy host callback and a root Attack.</summary>
    /// <param name="session">Native host session.</param>
    /// <param name="done">Condition observed after every completed step.</param>
    /// <param name="maxSteps">Upper bound of steps before the proof fails.</param>
    /// <returns>Number of steps consumed before the condition held.</returns>
    public static int AdvanceUntil(NativeMenuSession session, Func<bool> done, int maxSteps)
    {
        for (int step = 0; step < maxSteps; step++)
        {
            if (done())
            {
                return step;
            }

            ProbeRequirement.Require(session.Flow.Outcome == BattleOutcome.None, $"battle ended while advancing outcome={session.Flow.Outcome}");
            Step(session);
        }

        ProbeRequirement.Require(done(), $"condition not reached after {maxSteps} steps");
        return maxSteps;
    }

    /// <summary>Runs one ordinary phase step.</summary>
    /// <param name="session">Native host session.</param>
    public static void Step(NativeMenuSession session)
    {
        switch (session.Flow.Phase)
        {
            case BattleFlowPhase.EnemyTurn:
                session.EnemyAct();
                break;
            case BattleFlowPhase.ActionSequence:
                session.Flow.CompleteActionSequence();
                break;
            case BattleFlowPhase.PlayerCommand:
                session.Menu.OpenRoot(session.Flow.RootMenuLines());
                session.Tap(Key.Enter);
                break;
            default:
                throw new InvalidOperationException($"unexpected phase {session.Flow.Phase}");
        }
    }

    /// <summary>Completes the enemy turn that follows a spent player command and returns to the player's command phase.</summary>
    /// <param name="session">Native host session.</param>
    public static void ToPlayerCommand(NativeMenuSession session) =>
        AdvanceUntil(session, () => session.Flow.Phase == BattleFlowPhase.PlayerCommand, 8);
}
