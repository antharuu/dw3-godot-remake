using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests;

/// <summary>
/// Verifies that confirming a technique or digivolution from within their production submenus
/// routes through the real BattleMenuDriver→IBattleMenuDriverHost→BattleFlowController chain.
/// These are synthetic regression controls for the harness/consumer boundaries exercised by
/// NativeMenuCommandsProof.TechniqueExecution and DigivolveExecution in Godot headless.
/// </summary>
public sealed class BattleCommandExecutionTests
{
    // ── Technique execution through the driver ───────────────────────────────

    /// <summary>
    /// Confirming an enabled technique row from an open Technique submenu calls ChooseTechnique
    /// through the driver dispatch and debits MP from the active player.
    /// </summary>
    [Fact]
    public void ConfirmFromTechniqueSubmenu_ExecutesTechniqueAndDebitsMP()
    {
        var h = new TechniqueHarness();
        var driver = h.NewDriver();
        var player = h.Flow.State.ActivePlayerFighter;
        Assert.NotNull(player);
        int mpBefore = player.Mp;

        h.Input.PressAction();
        driver.Tick();

        Assert.True(
            h.Flow.Phase is BattleFlowPhase.EnemyTurn or BattleFlowPhase.ActionSequence,
            $"unexpected phase after technique execution phase={h.Flow.Phase}");
        Assert.Equal(mpBefore - TechniqueHarness.TechniqueMpCost, player.Mp);
    }

    /// <summary>
    /// A greyed technique (zero MP) confirmed from the submenu does not debit resources and
    /// leaves the phase at PlayerCommand.
    /// </summary>
    [Fact]
    public void ConfirmFromTechniqueSubmenu_ZeroMpTechnique_DoesNotDebitResources()
    {
        var h = new TechniqueHarness();
        var player = h.Flow.State.ActivePlayerFighter;
        Assert.NotNull(player);
        player.Mp = 0;
        h.ResetSubmenuWithDisabledRow();
        var driver = h.NewDriver();

        Assert.False(h.Menu.CanConfirm, "zero-mp technique row must be grey");

        h.Input.PressAction();
        driver.Tick();

        Assert.Equal(BattleFlowPhase.PlayerCommand, h.Flow.Phase);
        Assert.Equal(0, player.Mp);
    }

    // ── Digivolve execution through the driver ───────────────────────────────

    /// <summary>
    /// Confirming an available form from an open Digivolve submenu calls ChooseDigivolution
    /// through the driver dispatch, changes the active fighter form and spends the turn.
    /// </summary>
    [Fact]
    public void ConfirmFromDigivolveSubmenu_ExecutesFormChangeAndSpendsTurn()
    {
        var h = new DigivolveHarness();
        var driver = h.NewDriver();
        var player = h.Flow.State.ActivePlayerFighter;
        Assert.NotNull(player);
        int originalId = player.Id;

        Assert.False(h.Menu.CanConfirm, "current form at cursor 0 must be greyed");

        h.Input.SetDirections(left: false, right: false, up: false, down: true);
        driver.Tick();
        h.Input.SetDirections(left: false, right: false, up: false, down: false);
        driver.Tick();

        Assert.True(h.Menu.CanConfirm, "alternate form row must be enabled");
        Assert.Equal(1, h.Menu.Cursor);

        h.Input.PressAction();
        driver.Tick();

        Assert.Equal(BattleFlowPhase.EnemyTurn, h.Flow.Phase);
        var playerAfter = h.Flow.State.ActivePlayerFighter;
        Assert.NotNull(playerAfter);
        Assert.NotEqual(originalId, playerAfter.Id);
    }

    /// <summary>
    /// Confirming the current-form row (greyed) from the Digivolve submenu does not change the
    /// fighter identity and does not spend the turn.
    /// </summary>
    [Fact]
    public void ConfirmFromDigivolveSubmenu_SameForm_DoesNotChangeIdentityOrSpendTurn()
    {
        var h = new DigivolveHarness();
        var driver = h.NewDriver();
        var player = h.Flow.State.ActivePlayerFighter;
        Assert.NotNull(player);
        int originalId = player.Id;

        Assert.False(h.Menu.CanConfirm, "current form row must be greyed");

        h.Input.PressAction();
        driver.Tick();

        Assert.Equal(BattleFlowPhase.PlayerCommand, h.Flow.Phase);
        Assert.Equal(originalId, player.Id);
    }

    private static BattleFlowController BuildBaseFlow()
    {
        var setup = new BattleSetup();
        setup.Partners[0] = new PartnerSeed { Id = 1, Hp = 100, MaxHp = 100, Mp = 50, MaxMp = 50, Stats = Speed(999) };
        setup.Enemies[0] = new BattleEnemy { Fighter = 2, Hp = 999, Mp = 50, Strength = 16, BaseStats = Speed(1) };
        var flow = new BattleFlowController();
        flow.PlayerBasicTech = new TechData { Accuracy = 128, Power = 1 };
        flow.EnemyBasicTech = new TechData { Accuracy = 128, Power = 1 };
        flow.Start(BattleState.InitFighters(setup, digimon: 1, new ZeroRng()));
        return flow;
    }

    private static BattleStats Speed(int speed)
    {
        var stats = new BattleStats();
        stats.Stats[BattleStat.Speed] = speed;
        return stats;
    }

    private sealed class TechniqueHarness : IBattleMenuDriverHost
    {
        /// <summary>The MP cost shared between the fixture technique and the assertion.</summary>
        public const int TechniqueMpCost = 5;

        private static readonly TechData TechFixture = new()
        {
            Mp = TechniqueMpCost,
            Icon = BattleConst.TechPhysical,
        };

        private bool _prevUp;
        private bool _prevDown;
        private List<int> _subArgs;

        /// <summary>Initializes a new instance of the <see cref="TechniqueHarness"/> class.</summary>
        public TechniqueHarness()
        {
            Flow = BuildBaseFlow();
            Flow.PlayerTechniques = [new BattleTechnique(1, "tech", TechFixture)];
            _subArgs = [1];
            Menu.Open(BattleMenuKind.Technique, [new BattleMenuLine("tech-1", true)]);
        }

        /// <summary>Gets the semantic input bus.</summary>
        public InputState Input { get; } = new();

        /// <summary>Gets the menu state machine.</summary>
        public BattleMenuController Menu { get; } = new();

        /// <summary>Gets the battle flow controller.</summary>
        public BattleFlowController Flow { get; }

        /// <inheritdoc/>
        BattleFlowController IBattleMenuDriverHost.Flow => Flow;

        /// <inheritdoc/>
        BattleMenuController IBattleMenuDriverHost.Menu => Menu;

        /// <inheritdoc/>
        InputState IBattleMenuDriverHost.Input => Input;

        /// <inheritdoc/>
        IReadOnlyList<int> IBattleMenuDriverHost.SubmenuArguments => _subArgs;

        /// <summary>Creates and returns a new BattleMenuDriver bound to this harness.</summary>
        public BattleMenuDriver NewDriver() => new(this);

        /// <summary>Reopens the submenu with the first row disabled to simulate zero-MP affordability.</summary>
        public void ResetSubmenuWithDisabledRow()
        {
            _subArgs = [1];
            Menu.Open(BattleMenuKind.Technique, [new BattleMenuLine("tech-1", false)]);
        }

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
            if (Menu.Menu != BattleMenuKind.Root)
            {
                Menu.OpenRoot(Flow.RootMenuLines());
            }
        }

        /// <inheritdoc/>
        void IBattleMenuDriverHost.ConfirmRoot()
        {
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
    }

    private sealed class DigivolveHarness : IBattleMenuDriverHost
    {
        private readonly List<int> _subArgs;
        private bool _prevUp;
        private bool _prevDown;

        /// <summary>Initializes a new instance of the <see cref="DigivolveHarness"/> class.</summary>
        public DigivolveHarness()
        {
            Flow = BuildBaseFlow();
            int currentId = Flow.State.ActivePlayerFighter?.Id ?? 1;
            int alternateId = currentId == 1 ? 4 : 1;
            Flow.PlayerDigivolutions = [currentId, alternateId];
            _subArgs = [currentId, alternateId];
            var lines = new[]
            {
                new BattleMenuLine($"form-{currentId}", false),
                new BattleMenuLine($"form-{alternateId}", true),
            };
            Menu.Open(BattleMenuKind.Digivolve, lines);
        }

        /// <summary>Gets the semantic input bus.</summary>
        public InputState Input { get; } = new();

        /// <summary>Gets the menu state machine.</summary>
        public BattleMenuController Menu { get; } = new();

        /// <summary>Gets the battle flow controller.</summary>
        public BattleFlowController Flow { get; }

        /// <inheritdoc/>
        BattleFlowController IBattleMenuDriverHost.Flow => Flow;

        /// <inheritdoc/>
        BattleMenuController IBattleMenuDriverHost.Menu => Menu;

        /// <inheritdoc/>
        InputState IBattleMenuDriverHost.Input => Input;

        /// <inheritdoc/>
        IReadOnlyList<int> IBattleMenuDriverHost.SubmenuArguments => _subArgs;

        /// <summary>Creates and returns a new BattleMenuDriver bound to this harness.</summary>
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
            if (Menu.Menu != BattleMenuKind.Root)
            {
                Menu.OpenRoot(Flow.RootMenuLines());
            }
        }

        /// <inheritdoc/>
        void IBattleMenuDriverHost.ConfirmRoot()
        {
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
    }

    private sealed class ZeroRng : IBattleRng
    {
        /// <inheritdoc/>
        public int NextValue() => 0;
    }
}
