using Dw3Production.Diagnostics.Field;
using Dw3Production.Simulation;
using Godot;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Checks six-command root routing and unavailable rows through the real host.</summary>
internal static class NativeMenuCommandsProof
{
    /// <summary>Opens all four real submenus and checks that returning never spends a turn.</summary>
    public static void Openers()
    {
        BattleMenuKind[] pages = [BattleMenuKind.Technique, BattleMenuKind.Digivolve, BattleMenuKind.Switch, BattleMenuKind.Item];
        using var session = new NativeMenuSession();
        for (int round = 0; round < 3; round++)
        {
            for (int row = 1; row <= 4; row++)
            {
                Select(session, row);
                string before = Snapshot(session);
                session.KeyEvent(Key.Enter, true);
                session.KeyEvent(Key.Enter, true, echo: true);
                session.Tick();
                ProbeRequirement.Require(session.Menu.Menu == pages[row - 1], $"real opener failed row={row}");
                ProbeRequirement.Require(Snapshot(session) == before, $"opener or held Cross spent resources row={row}");
                session.KeyEvent(Key.Enter, false);
                session.Tap(Key.X);
                ProbeRequirement.Require(session.Menu.Menu == BattleMenuKind.Root && Snapshot(session) == before, $"real cancellation spent resources row={row}");
            }
        }

        GD.Print("NATIVE_ROOT_MENU_PROOF PASS openers=Technique-Digivolve-Switch-Item rounds=3 no-cost=true");
    }

    /// <summary>Checks root conditions, unaffordable techniques and empty collections without fabricated pages.</summary>
    public static void Unavailable()
    {
        using var session = new NativeMenuSession();
        var fighter = session.Flow.State.ActivePlayerFighter ?? throw new InvalidOperationException("fixture player missing");
        fighter.Flags = FighterFlags.Asleep | FighterFlags.Confused | FighterFlags.NoDigivolve | FighterFlags.NoSwitch;
        session.Menu.OpenRoot(session.Flow.RootMenuLines());
        string before = Snapshot(session);
        for (int row = 0; row < 6; row++)
        {
            Select(session, row);
            ProbeRequirement.Require(!session.Menu.CanConfirm, $"row should be grey row={row}");
            session.Tap(Key.Enter);
            ProbeRequirement.Require(Snapshot(session) == before && session.Menu.Menu == BattleMenuKind.Root, $"grey row consumed row={row}");
        }

        fighter.Flags = 0;
        fighter.Mp = 0;
        session.Menu.OpenRoot(session.Flow.RootMenuLines());
        Select(session, 1);
        session.Tap(Key.Enter);
        ProbeRequirement.Require(session.Menu.Menu == BattleMenuKind.Technique && !session.Menu.CanConfirm, "real technique affordability missing");
        before = Snapshot(session);
        session.Tap(Key.Enter);
        ProbeRequirement.Require(Snapshot(session) == before, "grey technique spent resources");
        session.Tap(Key.X);
        EmptyCollections(session);
        before = Snapshot(session);
        session.Menu.OpenRoot([]);
        session.Tap(Key.Down);
        session.Tap(Key.Enter);
        ProbeRequirement.Require(!session.Menu.CanConfirm && session.Menu.Lines.Count == 0 && Snapshot(session) == before, "empty root consumed");
        session.Menu.OpenRoot(session.Flow.RootMenuLines());
        ProbeRequirement.Require(session.Menu.Lines.Count == 6, "normal root reentry failed");
        GD.Print("NATIVE_ROOT_MENU_PROOF PASS conditions=grey-affordability-empty-root-empty-submenus-reentry");
    }

    /// <summary>Checks clamp, press edges and cancellation on the real root.</summary>
    public static void Navigation()
    {
        using var session = new NativeMenuSession();
        session.Tap(Key.Up);
        ProbeRequirement.Require(session.Menu.Cursor == 0, "root upper clamp failed");
        session.KeyEvent(Key.Down, true);
        session.KeyEvent(Key.Down, true, echo: true);
        session.Tick();
        ProbeRequirement.Require(session.Menu.Cursor == 1, "held/repeated direction moved twice");
        session.KeyEvent(Key.Down, false);
        for (int index = 0; index < 8; index++)
        {
            session.Tap(Key.Down);
        }

        ProbeRequirement.Require(session.Menu.Cursor == 5, "root lower clamp failed");
        session.Tap(Key.Up);
        ProbeRequirement.Require(session.Menu.Cursor == 4, "root up edge failed");
        string before = Snapshot(session);
        session.Tap(Key.X);
        ProbeRequirement.Require(session.Menu.Menu == BattleMenuKind.Root && Snapshot(session) == before, "root cancel consumed");
        GD.Print("NATIVE_ROOT_MENU_PROOF PASS navigation=up-down-clamp-hold-echo-root-cancel");
    }

    /// <summary>Navigates from root start using real press/release gestures.</summary>
    /// <param name="session">Native host fixture.</param>
    /// <param name="row">Desired root row.</param>
    public static void Select(NativeMenuSession session, int row)
    {
        for (int index = 0; index < row; index++)
        {
            session.Tap(Key.Down);
        }
    }

    /// <summary>Captures resources and scheduler state at the actual simulation consumer.</summary>
    /// <param name="session">Native host fixture.</param>
    /// <returns>Stable resource snapshot, including all fighters and the random draw count.</returns>
    public static string Snapshot(NativeMenuSession session) =>
        $"{session.Flow.Phase}/{session.Flow.Outcome}/{session.Flow.State.NextActor}/{session.Rng.Count}/{session.Flow.State.ActivePlayer}/{session.Flow.State.ActiveEnemy}/"
        + string.Join(',', session.Flow.State.Players.Concat(session.Flow.State.Enemies).Select(fighter => $"{fighter?.Id}:{fighter?.Hp}:{fighter?.Mp}"))
        + string.Join(',', session.Flow.ItemStock.Select(entry => $"{entry.Key}:{entry.Value}"));

    /// <summary>Tries empty catalogues through their production openers.</summary>
    /// <param name="session">Native host fixture.</param>
    private static void EmptyCollections(NativeMenuSession session)
    {
        session.Flow.PlayerTechniques = [];
        session.Flow.PlayerDigivolutions = [];
        session.Flow.ItemStock.Clear();
        session.Flow.State.Players[1] = null;
        string before = Snapshot(session);
        for (int row = 1; row <= 4; row++)
        {
            session.Menu.OpenRoot(session.Flow.RootMenuLines());
            Select(session, row);
            session.Tap(Key.Enter);
            ProbeRequirement.Require(session.Menu.Menu == BattleMenuKind.Root && Snapshot(session) == before, $"empty opener consumed row={row}");
        }
    }
}
