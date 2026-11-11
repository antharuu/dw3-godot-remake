using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests;

/// <summary>
/// Verifies root-menu navigation using the real BattleMenuDriver fed by real InputState semantic
/// inputs. Every scenario injects Cross/Circle/Down/Up via the same InputState the driver reads
/// and observes effects at the BattleFlowController consumer — Flow.Choose is never called
/// directly as a proof substitute.
/// </summary>
public sealed class BattleMenuRootNavigationTests
{
    // ── 1 : Navigation, confirmation, and atomic turn cost ───────────────

    /// <summary>Pressing Cross on cursor 0 executes the Attack command and spends exactly one turn.</summary>
    [Fact]
    public void Confirm_AtCursorZero_ExecutesAttackAndSpendsTurn()
    {
        var h = new Harness();
        var driver = h.NewDriver();

        h.Input.PressAction();
        driver.Tick();

        Assert.Equal(BattleFlowPhase.EnemyTurn, h.Flow.Phase);
        Assert.Equal(1, h.ConfirmRootCount);
        Assert.Equal(BattleCommand.Attack, h.LastConfirmedCommand);
    }

    /// <summary>Navigating to cursor 5 with Down edges then pressing Cross executes Flee.</summary>
    [Fact]
    public void Confirm_AtCursorFive_ExecutesFlee()
    {
        var h = new Harness();
        var driver = h.NewDriver();

        for (var i = 0; i < 5; i++)
        {
            h.Input.SetDirections(left: false, right: false, up: false, down: true);
            driver.Tick();
            h.Input.SetDirections(left: false, right: false, up: false, down: false);
            driver.Tick(); // process release so the next press registers as a new edge
        }

        Assert.Equal(5, h.Menu.Cursor);
        h.Input.PressAction();
        driver.Tick();

        Assert.Equal(BattleFlowPhase.Ended, h.Flow.Phase);
        Assert.Equal(BattleOutcome.PlayerFled, h.Flow.State.Outcome);
        Assert.Equal(1, h.ConfirmRootCount);
        Assert.Equal(BattleCommand.Flee, h.LastConfirmedCommand);
    }

    /// <summary>Navigating to cursor 1 and pressing Cross opens the Technique submenu without spending the turn.</summary>
    [Fact]
    public void Confirm_AtCursorOne_OpensTechniqueSubmenu()
    {
        var h = new Harness();
        var driver = h.NewDriver();

        h.Input.SetDirections(left: false, right: false, up: false, down: true);
        driver.Tick();
        h.Input.SetDirections(left: false, right: false, up: false, down: false);

        h.Input.PressAction();
        driver.Tick();

        Assert.Equal(BattleMenuKind.Technique, h.Menu.Menu);
        Assert.Equal(BattleFlowPhase.PlayerCommand, h.Flow.Phase);
        Assert.Equal(1, h.ConfirmRootCount);
    }

    /// <summary>Navigating to cursor 2 and pressing Cross opens the Digivolve submenu without spending the turn.</summary>
    [Fact]
    public void Confirm_AtCursorTwo_OpensDigivolveSubmenu()
    {
        var h = new Harness();
        var driver = h.NewDriver();

        for (var i = 0; i < 2; i++)
        {
            h.Input.SetDirections(left: false, right: false, up: false, down: true);
            driver.Tick();
            h.Input.SetDirections(left: false, right: false, up: false, down: false);
            driver.Tick(); // process release
        }

        h.Input.PressAction();
        driver.Tick();

        Assert.Equal(BattleMenuKind.Digivolve, h.Menu.Menu);
        Assert.Equal(BattleFlowPhase.PlayerCommand, h.Flow.Phase);
    }

    // ── 2 : Cursor movement (press-edge) ─────────────────────────────────

    /// <summary>One Down press-edge moves the cursor from 0 to 1.</summary>
    [Fact]
    public void NavigateDown_OneEdge_IncrementsCursorByOne()
    {
        var h = new Harness();
        var driver = h.NewDriver();

        h.Input.SetDirections(left: false, right: false, up: false, down: true);
        driver.Tick();

        Assert.Equal(1, h.Menu.Cursor);
    }

    /// <summary>One Up press-edge from cursor 2 decrements the cursor to 1.</summary>
    [Fact]
    public void NavigateUp_OneEdge_DecrementsCursorByOne()
    {
        var h = new Harness();
        var driver = h.NewDriver();

        for (var i = 0; i < 2; i++)
        {
            h.Input.SetDirections(left: false, right: false, up: false, down: true);
            driver.Tick();
            h.Input.SetDirections(left: false, right: false, up: false, down: false);
            driver.Tick(); // process release
        }

        h.Input.SetDirections(left: false, right: false, up: true, down: false);
        driver.Tick();

        Assert.Equal(1, h.Menu.Cursor);
    }

    /// <summary>Holding Down without releasing fires only the first press-edge — a held direction must not repeat.</summary>
    [Fact]
    public void HeldDown_SecondTick_DoesNotMoveCursorAgain()
    {
        var h = new Harness();
        var driver = h.NewDriver();

        h.Input.SetDirections(left: false, right: false, up: false, down: true);
        driver.Tick();
        var cursorAfterFirstTick = h.Menu.Cursor;

        driver.Tick(); // still held — no release

        Assert.Equal(1, cursorAfterFirstTick);
        Assert.Equal(1, h.Menu.Cursor);
    }

    /// <summary>Down at the last row is a no-op: cursor stays clamped at 5.</summary>
    [Fact]
    public void NavigateDown_AtLastRow_ClampsCursor()
    {
        var h = new Harness();
        var driver = h.NewDriver();

        for (var i = 0; i < 10; i++)
        {
            h.Input.SetDirections(left: false, right: false, up: false, down: true);
            driver.Tick();
            h.Input.SetDirections(left: false, right: false, up: false, down: false);
            driver.Tick(); // process release
        }

        Assert.Equal(5, h.Menu.Cursor);
    }

    /// <summary>Up at row 0 is a no-op: cursor stays clamped at 0.</summary>
    [Fact]
    public void NavigateUp_AtFirstRow_ClampsCursor()
    {
        var h = new Harness();
        var driver = h.NewDriver();

        for (var i = 0; i < 5; i++)
        {
            h.Input.SetDirections(left: false, right: false, up: true, down: false);
            driver.Tick();
            h.Input.SetDirections(left: false, right: false, up: false, down: false);
            driver.Tick(); // process release
        }

        Assert.Equal(0, h.Menu.Cursor);
    }

    // ── 3 : Hold and double-action prevention ────────────────────────────

    /// <summary>
    /// Holding Cross across two ticks fires confirm only once: the one-shot ActionPressed flag is
    /// cleared by ConsumeActionPressed on the first Tick, so the second Tick sees no pending press.
    /// </summary>
    [Fact]
    public void HeldCross_SecondTick_DoesNotRepeatCommand()
    {
        var h = new Harness();
        var driver = h.NewDriver();

        h.Input.PressAction(); // ActionPressed=true, ActionHeld=true

        driver.Tick(); // consumes ActionPressed → Attack → EnemyTurn

        h.Flow.EnemyAct(new ZeroRng());
        Assert.Equal(BattleFlowPhase.PlayerCommand, h.Flow.Phase);
        h.Menu.OpenRoot(h.Flow.RootMenuLines());

        h.Input.PressAction(); // still held → ActionPressed stays false
        var confirmsBefore = h.ConfirmRootCount;
        driver.Tick();

        Assert.Equal(confirmsBefore, h.ConfirmRootCount);
    }

    /// <summary>
    /// Focus loss clears all held directions; the first tick after regain must not see a Down edge
    /// because nothing was held at the moment of regain.
    /// </summary>
    [Fact]
    public void FocusLoss_ClearsHeldDirection_NoSpuriousEdge()
    {
        var h = new Harness();
        var driver = h.NewDriver();

        h.Input.SetDirections(left: false, right: false, up: false, down: true);
        driver.Tick(); // cursor moves to 1
        h.Input.Clear(); // focus lost

        driver.Tick(); // tick with nothing held

        Assert.Equal(1, h.Menu.Cursor);
    }

    /// <summary>
    /// Focus loss clears the held Cross state; regaining focus and pressing once fires exactly one confirm.
    /// </summary>
    [Fact]
    public void FocusLoss_ClearsHeldCross_RegainAndPressOnce_ExactlyOneConfirm()
    {
        var h = new Harness();
        var driver = h.NewDriver();

        h.Input.PressAction(); // ActionHeld=true, ActionPressed=true
        h.Input.Clear();       // focus lost — ActionHeld reset

        driver.Tick(); // no pending press → no confirm

        Assert.Equal(0, h.ConfirmRootCount);

        h.Input.PressAction(); // fresh press after regain
        driver.Tick();

        Assert.Equal(1, h.ConfirmRootCount);
    }

    // ── 4 : Greyed commands and non-interactive phase ─────────────────────

    /// <summary>Pressing Cross on a greyed (disabled) command does nothing: turn is not spent.</summary>
    [Fact]
    public void Confirm_OnGreyedCommand_DoesNothing()
    {
        var h = new Harness();
        var driver = h.NewDriver();

        var fighter = Assert.IsType<BattleFighter>(h.Flow.State.ActivePlayerFighter);
        fighter.Flags |= FighterFlags.Asleep;
        h.Menu.OpenRoot(h.Flow.RootMenuLines());

        Assert.False(h.Menu.CanConfirm);

        h.Input.PressAction();
        driver.Tick();

        Assert.Equal(BattleFlowPhase.PlayerCommand, h.Flow.Phase);
        Assert.Equal(0, h.ConfirmRootCount);
    }

    /// <summary>When it is the enemy's turn the driver does nothing: inputs are ignored, no confirm fires.</summary>
    [Fact]
    public void EnemyPhaseTick_IgnoresInputAndFiresNoConfirm()
    {
        var h = new Harness();
        var driver = h.NewDriver();

        h.Input.PressAction();
        driver.Tick(); // Attack chosen → EnemyTurn
        Assert.Equal(BattleFlowPhase.EnemyTurn, h.Flow.Phase);

        h.Input.PressAction();
        driver.Tick();

        Assert.Equal(1, h.ConfirmRootCount);
    }

    // ── 5 : Cancel and submenu back-navigation ────────────────────────────

    /// <summary>Pressing Circle from the root menu calls CancelMenu once and the menu stays at root.</summary>
    [Fact]
    public void Cancel_AtRoot_CallsCancelAndStaysAtRoot()
    {
        var h = new Harness();
        var driver = h.NewDriver();

        h.Input.PressCancel();
        driver.Tick();

        Assert.Equal(1, h.CancelCount);
        Assert.Equal(BattleMenuKind.Root, h.Menu.Menu);
    }

    /// <summary>Pressing Circle from a submenu returns to the root menu without spending the turn.</summary>
    [Fact]
    public void Cancel_FromSubMenu_ReturnsToRoot()
    {
        var h = new Harness();
        var driver = h.NewDriver();

        h.Input.SetDirections(left: false, right: false, up: false, down: true);
        driver.Tick();
        h.Input.SetDirections(left: false, right: false, up: false, down: false);
        h.Input.PressAction();
        driver.Tick();
        Assert.Equal(BattleMenuKind.Technique, h.Menu.Menu);

        h.Input.PressCancel();
        driver.Tick();

        Assert.Equal(BattleMenuKind.Root, h.Menu.Menu);
        Assert.Equal(BattleFlowPhase.PlayerCommand, h.Flow.Phase);
    }

    /// <summary>Repeated open-then-cancel transitions accumulate exactly N confirms and N cancels with no leaked turn.</summary>
    [Fact]
    public void RepeatedTransition_NOpenClose_ExactCountsNoCumulativeEffects()
    {
        const int Rounds = 3;
        var h = new Harness();
        var driver = h.NewDriver();

        for (var round = 0; round < Rounds; round++)
        {
            h.Input.SetDirections(left: false, right: false, up: false, down: true);
            driver.Tick();
            h.Input.SetDirections(left: false, right: false, up: false, down: false);
            driver.Tick(); // process release

            h.Input.PressAction();
            driver.Tick();
            h.Input.ReleaseAction(); // release so the next round can re-arm Cross
            Assert.Equal(BattleMenuKind.Technique, h.Menu.Menu);

            h.Input.PressCancel();
            driver.Tick();
            h.Input.ReleaseCancel(); // release so the next round can re-arm Circle
            Assert.Equal(BattleMenuKind.Root, h.Menu.Menu);

            h.Input.SetDirections(left: false, right: false, up: true, down: false);
            driver.Tick();
            h.Input.SetDirections(left: false, right: false, up: false, down: false);
            driver.Tick(); // process release
        }

        Assert.Equal(Rounds, h.ConfirmRootCount);
        Assert.Equal(Rounds, h.CancelCount);
        Assert.Equal(BattleFlowPhase.PlayerCommand, h.Flow.Phase);
    }

    // ── 6 : Device-change press-edge isolation ────────────────────────────

    /// <summary>
    /// Releasing Down and pressing it again (simulating a source change or re-press) generates a
    /// second press-edge and moves the cursor again — the edge detector uses WasDownHeld correctly.
    /// </summary>
    [Fact]
    public void DeviceChange_ReleaseThenRepress_GeneratesSecondEdge()
    {
        var h = new Harness();
        var driver = h.NewDriver();

        h.Input.SetDirections(left: false, right: false, up: false, down: true);
        driver.Tick(); // cursor = 1
        h.Input.SetDirections(left: false, right: false, up: false, down: false);
        driver.Tick(); // released tick — no movement
        h.Input.SetDirections(left: false, right: false, up: false, down: true);
        driver.Tick(); // new press — cursor = 2

        Assert.Equal(2, h.Menu.Cursor);
    }

    // ══════════════════════════════════════════════════════════════════════
    // Test harness
    // ══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Minimal test double for IBattleMenuDriverHost. Owns real InputState, BattleMenuController and
    /// BattleFlowController, and routes ConfirmRoot/CancelMenu identically to the production
    /// coordinator (Attack and Flee call flow.Choose; submenu commands open a stub page so the
    /// driver's submenu dispatch path is reachable without a full catalog).
    /// </summary>
    private sealed class Harness : IBattleMenuDriverHost
    {
        private readonly List<int> _submenuArgs = [];
        private bool _prevUp;
        private bool _prevDown;

        /// <summary>Initializes a new instance of the <see cref="Harness"/> class.</summary>
        public Harness()
        {
            Flow = BuildFlow();
            Menu.OpenRoot(Flow.RootMenuLines());
        }

        /// <summary>Gets the semantic input bus shared with the driver.</summary>
        public InputState Input { get; } = new();

        /// <summary>Gets the menu state machine shared with the driver.</summary>
        public BattleMenuController Menu { get; } = new();

        /// <summary>Gets the battle flow controller observed by the tests.</summary>
        public BattleFlowController Flow { get; }

        /// <summary>Gets how many times ConfirmRoot was called by the driver.</summary>
        public int ConfirmRootCount { get; private set; }

        /// <summary>Gets how many times CancelMenu was called by the driver.</summary>
        public int CancelCount { get; private set; }

        /// <summary>Gets the last BattleCommand the driver dispatched through ConfirmRoot.</summary>
        public BattleCommand? LastConfirmedCommand { get; private set; }

        // ── IBattleMenuDriverHost (properties) ───────────────────────────

        /// <inheritdoc/>
        BattleFlowController IBattleMenuDriverHost.Flow => Flow;

        /// <inheritdoc/>
        BattleMenuController IBattleMenuDriverHost.Menu => Menu;

        /// <inheritdoc/>
        InputState IBattleMenuDriverHost.Input => Input;

        /// <inheritdoc/>
        IReadOnlyList<int> IBattleMenuDriverHost.SubmenuArguments => _submenuArgs;

        // ── Methods ───────────────────────────────────────────────────────

        /// <summary>Returns a new BattleMenuDriver bound to this harness.</summary>
        public BattleMenuDriver NewDriver() => new(this);

        /// <inheritdoc/>
        bool IBattleMenuDriverHost.WasUpHeld() => _prevUp;

        /// <inheritdoc/>
        bool IBattleMenuDriverHost.WasDownHeld() => _prevDown;

        /// <inheritdoc/>
        void IBattleMenuDriverHost.RememberHeldDirections(bool up, bool down)
        {
            _prevUp = up;
            _prevDown = down;
        }

        /// <inheritdoc/>
        void IBattleMenuDriverHost.RefreshActivePartnerCatalogs()
        {
        }

        /// <inheritdoc/>
        void IBattleMenuDriverHost.OpenForcedSwitchMenu()
        {
        }

        /// <inheritdoc/>
        void IBattleMenuDriverHost.CancelMenu()
        {
            CancelCount++;
            if (Menu.Menu != BattleMenuKind.Root)
            {
                Menu.OpenRoot(Flow.RootMenuLines());
            }
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Mirrors BattleMenuCoordinator.ConfirmRoot: cursor 0 → Attack, cursor 5 → Flee (both
        /// through flow.Choose); cursors 1-4 open a minimal stub submenu so the driver reaches
        /// its submenu confirm path. Cards 044/045/046 own the real submenu effects.
        /// </remarks>
        void IBattleMenuDriverHost.ConfirmRoot()
        {
            ConfirmRootCount++;
            LastConfirmedCommand = (BattleCommand)Menu.Cursor;
            switch (Menu.Cursor)
            {
                case 0:
                    Flow.Choose(BattleCommand.Attack, 0, new ZeroRng());
                    break;

                case 1:
                    _submenuArgs.Clear();
                    _submenuArgs.Add(1);
                    Menu.Open(BattleMenuKind.Technique, [new BattleMenuLine("tech-1", true)]);
                    break;

                case 2:
                    _submenuArgs.Clear();
                    _submenuArgs.Add(2);
                    Menu.Open(BattleMenuKind.Digivolve, [new BattleMenuLine("digi-1", true)]);
                    break;

                case 3:
                    _submenuArgs.Clear();
                    _submenuArgs.Add(1);
                    Menu.Open(BattleMenuKind.Switch, [new BattleMenuLine("switch-1", true)]);
                    break;

                case 4:
                    _submenuArgs.Clear();
                    _submenuArgs.Add(42);
                    Menu.Open(BattleMenuKind.Item, [new BattleMenuLine("item-1", true)]);
                    break;

                case 5:
                    Flow.Choose(BattleCommand.Flee, 0, new ZeroRng());
                    break;
            }
        }

        /// <inheritdoc/>
        void IBattleMenuDriverHost.ChooseTechnique(int argument) =>
            Flow.Choose(BattleCommand.Technique, argument, new ZeroRng());

        /// <inheritdoc/>
        void IBattleMenuDriverHost.ChooseDigivolution(int argument) =>
            Flow.Choose(BattleCommand.Digivolve, argument, new ZeroRng());

        /// <inheritdoc/>
        void IBattleMenuDriverHost.OpenSwitchIn(int argument)
        {
        }

        /// <inheritdoc/>
        void IBattleMenuDriverHost.OpenPairSwitch(int argument)
        {
        }

        /// <inheritdoc/>
        void IBattleMenuDriverHost.ConfirmPairSwitch()
        {
        }

        /// <inheritdoc/>
        void IBattleMenuDriverHost.ChooseItem(int argument) =>
            Flow.Choose(BattleCommand.Item, argument, new ZeroRng());

        /// <summary>Builds the smallest valid battle flow for the navigation tests.</summary>
        private static BattleFlowController BuildFlow()
        {
            var setup = new BattleSetup();
            setup.Partners[0] = new PartnerSeed
            {
                Id = 1,
                Hp = 100,
                MaxHp = 100,
                Mp = 50,
                MaxMp = 50,
                Stats = PlayerStats(),
            };
            setup.Enemies[0] = new BattleEnemy
            {
                Fighter = 2,
                Hp = 999,
                Mp = 50,
                Strength = 16,
                BaseStats = EnemyStats(),
            };

            var flow = new BattleFlowController();
            flow.PlayerBasicTech = new TechData { Accuracy = 128, Power = 1 };
            flow.EnemyBasicTech = new TechData { Accuracy = 128, Power = 1 };
            flow.Start(BattleState.InitFighters(setup, digimon: 1, new ZeroRng()));
            return flow;
        }

        /// <summary>Returns stats for the player: high speed ensures player acts first regardless of opening RNG.</summary>
        private static BattleStats PlayerStats()
        {
            var stats = new BattleStats();
            stats.Stats[BattleStat.Attack] = 1;
            stats.Stats[BattleStat.Defense] = 999;
            stats.Stats[BattleStat.Speed] = 999;
            return stats;
        }

        /// <summary>Returns minimal stats for the enemy (low speed, high HP so it survives repeated attacks).</summary>
        private static BattleStats EnemyStats()
        {
            var stats = new BattleStats();
            stats.Stats[BattleStat.Attack] = 1;
            stats.Stats[BattleStat.Defense] = 999;
            stats.Stats[BattleStat.Speed] = 1;
            return stats;
        }
    }

    /// <summary>RNG that always returns 0 (deterministic hits, no misses).</summary>
    private sealed class ZeroRng : IBattleRng
    {
        /// <inheritdoc/>
        public int NextValue() => 0;
    }
}
