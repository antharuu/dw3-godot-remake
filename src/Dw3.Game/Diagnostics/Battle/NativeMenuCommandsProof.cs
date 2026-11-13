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

    /// <summary>Checks source-sized technique pages and empty technique and digivolution panes through normal host input.</summary>
    public static void Lists()
    {
        using var session = new NativeMenuSession();
        Select(session, 1);
        session.Tap(Key.Enter);
        ProbeRequirement.Require(session.Menu.Menu == BattleMenuKind.Technique, "technique list did not open");
        ProbeRequirement.Require(session.Menu.PageCount == 2 && session.Menu.View.Lines.Count == 6, "technique first page is not source-sized");
        session.Tap(Key.Pagedown);
        ProbeRequirement.Require(session.Menu.First == 6 && session.Menu.Cursor == 0 && session.Menu.View.Lines.Count == 1, "technique last page is not reachable");
        session.Tap(Key.X);

        session.Flow.PlayerTechniques = [];
        session.Menu.OpenRoot(session.Flow.RootMenuLines());
        Select(session, 1);
        session.Tap(Key.Enter);
        ProbeRequirement.Require(session.Menu.Menu == BattleMenuKind.Technique && session.Menu.Lines.Count == 0, "empty technique pane did not open");
        session.Tap(Key.X);

        session.Flow.PlayerDigivolutions = [];
        session.Menu.OpenRoot(session.Flow.RootMenuLines());
        Select(session, 2);
        session.Tap(Key.Enter);
        ProbeRequirement.Require(session.Menu.Menu == BattleMenuKind.Digivolve && session.Menu.Lines.Count == 0, "empty digivolution pane did not open");
        session.Tap(Key.X);
        ProbeRequirement.Require(session.Menu.Menu == BattleMenuKind.Root, "empty list cancellation did not return to root");
        GD.Print("NATIVE_ROOT_MENU_PROOF PASS lists=pagination-last-page-empty-technique-empty-digivolve-cancel");
    }

    /// <summary>
    /// Confirms a technique from the technique submenu through production host ingress, then proves the
    /// grey zero-MP refusal and cancellation do not debit resources.
    /// </summary>
    public static void TechniqueExecution()
    {
        TechniqueHappyPath();

        // ── refusal path: zero-MP technique is grey, resources unchanged ────
        using var refusal = new NativeMenuSession();
        var refPlayer = refusal.Player;
        refPlayer.Mp = 0;
        refusal.Menu.OpenRoot(refusal.Flow.RootMenuLines());
        string beforeRefusal = Snapshot(refusal);

        Select(refusal, 1);
        refusal.Tap(Key.Enter);
        ProbeRequirement.Require(refusal.Menu.Menu == BattleMenuKind.Technique, "zero-mp technique submenu did not open");
        ProbeRequirement.Require(!refusal.Menu.CanConfirm, "zero-mp technique row must be grey");
        refusal.Tap(Key.Enter);
        ProbeRequirement.Require(Snapshot(refusal) == beforeRefusal, "grey zero-mp technique consumed resources");
        refusal.Tap(Key.X);
        ProbeRequirement.Require(refusal.Menu.Menu == BattleMenuKind.Root && Snapshot(refusal) == beforeRefusal, "technique cancel consumed resources");

        GD.Print("NATIVE_ROOT_MENU_PROOF PASS technique=zero-mp-grey-refusal cancel=no-cost ingress=GameHost._Input");
    }

    /// <summary>
    /// Confirms a form change from the digivolution submenu through production host ingress and
    /// verifies the fighter identity changes and the turn is spent. Also proves the current-form row
    /// is greyed and that cancel returns to root without cost.
    /// </summary>
    public static void DigivolveExecution()
    {
        // ── happy path: change to form 4 spends a turn ──────────────────────
        using var session = new NativeMenuSession();
        var player = session.Player;
        int originalId = player.Id;
        string snapshotBefore = Snapshot(session);

        Select(session, 2);
        session.Tap(Key.Enter);
        ProbeRequirement.Require(session.Menu.Menu == BattleMenuKind.Digivolve, "digivolve submenu did not open from row 2");
        ProbeRequirement.Require(!session.Menu.CanConfirm, $"current form at cursor 0 must be greyed id={originalId}");
        ProbeRequirement.Require(Snapshot(session) == snapshotBefore, "opening digivolve submenu changed resources");

        session.Tap(Key.Down);
        ProbeRequirement.Require(session.Menu.Cursor == 1 && session.Menu.CanConfirm, "form 4 at cursor 1 must be enabled");
        ProbeRequirement.Require(Snapshot(session) == snapshotBefore, "cursor navigation to form 4 changed resources");

        session.Tap(Key.Enter);

        ProbeRequirement.Require(player.Id == 4, $"digivolve did not change fighter id={player.Id}");
        ProbeRequirement.Require(
            session.Flow.Phase == BattleFlowPhase.EnemyTurn,
            $"digivolve did not spend turn phase={session.Flow.Phase}");
        ProbeRequirement.Require(Snapshot(session) != snapshotBefore, "digivolve execution left snapshot unchanged");

        GD.Print($"NATIVE_ROOT_MENU_PROOF PASS digivolve=execution form={originalId}→{player.Id} phase=EnemyTurn ingress=GameHost._Input");

        // ── cancel path: open submenu then X returns to root with no cost ────
        using var cancel = new NativeMenuSession();
        string cancelBefore = Snapshot(cancel);
        Select(cancel, 2);
        cancel.Tap(Key.Enter);
        ProbeRequirement.Require(cancel.Menu.Menu == BattleMenuKind.Digivolve, "digivolve cancel test: submenu did not open");
        cancel.Tap(Key.X);
        ProbeRequirement.Require(cancel.Menu.Menu == BattleMenuKind.Root, "digivolve cancel did not return to root");
        ProbeRequirement.Require(Snapshot(cancel) == cancelBefore, "digivolve cancel consumed resources");

        GD.Print("NATIVE_ROOT_MENU_PROOF PASS digivolve=cancel no-cost ingress=GameHost._Input");
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
        BattleMenuKind[] menus = [BattleMenuKind.Technique, BattleMenuKind.Digivolve, BattleMenuKind.Switch, BattleMenuKind.Item];
        for (int row = 1; row <= 4; row++)
        {
            session.Menu.OpenRoot(session.Flow.RootMenuLines());
            Select(session, row);
            session.Tap(Key.Enter);
            ProbeRequirement.Require(session.Menu.Menu == menus[row - 1] && session.Menu.Lines.Count == 0 && Snapshot(session) == before, $"empty opener was not an empty pane row={row}");
            session.Tap(Key.X);
            ProbeRequirement.Require(session.Menu.Menu == BattleMenuKind.Root && Snapshot(session) == before, $"empty pane cancellation consumed row={row}");
        }
    }

    /// <summary>
    /// SYNTHETIC qualified positive technique (MP 5, accuracy 128, power 22 as in the qualified gauge vector, script 5) against a SYNTHETIC defense of 1 confirmed through host input:
    /// observes the MP debit, the damage applied to the enemy, the ordered draws of the production hit consumer and
    /// the captured action sequence.
    /// </summary>
    private static void TechniqueHappyPath()
    {
        using var session = new NativeMenuSession(fixture: new(setup => NativeMenuFixture.Enemy(setup).BaseStats.Stats[BattleStat.Defense] = 1));
        var player = session.Player;
        var enemy = session.Enemy;
        session.Flow.PlayerTechniques = [new BattleTechnique(7, "synthetic", new TechData { Mp = 5, Accuracy = 128, Power = 22, HitCount = 1, Script = 5 })];
        int mpBefore = player.Mp;
        int enemyHpBefore = enemy.Hp;
        int draws = session.Rng.Count;

        Select(session, 1);
        session.Tap(Key.Enter);
        ProbeRequirement.Require(session.Menu.Menu == BattleMenuKind.Technique && session.Menu.CanConfirm, "technique submenu row 1 not available before execution");
        ProbeRequirement.Require(session.Rng.Count == draws && enemy.Hp == enemyHpBefore, "technique navigation consumed draws or damaged");
        session.Tap(Key.Enter);

        ProbeRequirement.Require(session.Flow.Phase == BattleFlowPhase.ActionSequence && session.Flow.PendingSequenceContext is { AttackerId: 1, TargetId: 2 }, $"technique execution did not reach its sequence phase={session.Flow.Phase}");
        ProbeRequirement.Require(player.Mp == mpBefore - 5, $"technique did not debit exactly 5 MP before={mpBefore} after={player.Mp}");
        ProbeRequirement.Require(enemy.Hp == enemyHpBefore - 22, $"technique damage oracle 22 failed enemy-hp={enemyHpBefore}->{enemy.Hp}");
        ProbeRequirement.Require(session.Rng.DrawsSince(draws) == "[0]", $"technique ordered draws {session.Rng.DrawsSince(draws)} differ from the single accuracy draw [0]");
        GD.Print($"NATIVE_ROOT_MENU_PROOF PASS technique=execution fixture=SYNTHETIC phase=ActionSequence mp={mpBefore}->{player.Mp} enemy-hp={enemyHpBefore}->{enemy.Hp} rng={session.Rng.DrawsSince(draws)} script=5 ingress=GameHost._Input");
    }
}
