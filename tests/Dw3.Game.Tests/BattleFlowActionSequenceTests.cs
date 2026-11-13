using Dw3.Data.Battle;
using Dw3Production;
using Dw3Production.Conversion;
using Dw3Production.Simulation;
using Xunit;
using BattleEnemy = Dw3Production.Simulation.BattleEnemy;
using TechData = Dw3Production.Simulation.TechData;

namespace Dw3.Game.Tests;

/// <summary>End-to-end integration tests for the action-sequence phase:
/// command seam → ActionSequence phase → tick clock → completion and state.
/// Uses fabricated fixtures only — no real data pack required.</summary>
public sealed class BattleFlowActionSequenceTests
{
    // ── Criterion 6: end-to-end via real seam ────────────────────────────────
    [Fact]
    public void Choose_WithScript_TransitionsToActionSequence()
    {
        var flow = BuildFlowOnPlayerTurn();

        flow.Choose(BattleCommand.Attack, 0, new FixedRng());

        Assert.Equal(BattleFlowPhase.ActionSequence, flow.Phase);
    }

    [Fact]
    public void Tick_AdvancesSequenceToCompletion()
    {
        var flow = BuildFlowOnPlayerTurn();
        var script = ScriptWith((BattleActionOpcode.End, []));
        var host = new SequenceTrackingHost(flow, new StubScriptProvider(script));
        var coordinator = new BattleTickCoordinator(host);

        flow.Choose(BattleCommand.Attack, 0, new FixedRng());
        Assert.Equal(BattleFlowPhase.ActionSequence, flow.Phase);

        // Single tick should complete the End-only script
        coordinator.Tick();

        ActionResultTestSteps.CloseResult(flow, coordinator.Tick);
        Assert.NotEqual(BattleFlowPhase.ActionSequence, flow.Phase);
    }

    [Fact]
    public void NegativeWait_SequenceWaitsOneFrameBeforeEnd()
    {
        var flow = BuildFlowOnPlayerTurn();
        var script = ScriptWith((BattleActionOpcode.Wait, [1]), (BattleActionOpcode.End, []));
        var host = new SequenceTrackingHost(flow, new StubScriptProvider(script));
        var coordinator = new BattleTickCoordinator(host);

        flow.Choose(BattleCommand.Attack, 0, new FixedRng());

        // Tick 1: Wait starts (1 frame), not complete yet
        coordinator.Tick();
        Assert.Equal(BattleFlowPhase.ActionSequence, flow.Phase);

        // Tick 2: Wait expires, End fires
        coordinator.Tick();
        ActionResultTestSteps.CloseResult(flow, coordinator.Tick);
        Assert.NotEqual(BattleFlowPhase.ActionSequence, flow.Phase);
    }

    [Fact]
    public void NullScriptProvider_DoesNotCompleteRequiredSequence()
    {
        var flow = BuildFlowOnPlayerTurn();
        var host = new SequenceTrackingHost(flow, new StubScriptProvider(null));
        var coordinator = new BattleTickCoordinator(host);

        flow.Choose(BattleCommand.Attack, 0, new FixedRng());
        Assert.Equal(BattleFlowPhase.ActionSequence, flow.Phase);

        coordinator.Tick();
        coordinator.Tick();
        Assert.Equal(BattleFlowPhase.ActionSequence, flow.Phase);
        Assert.NotNull(flow.PendingSequenceContext);
    }

    [Fact]
    public void HitEvents_FrozenBeforeRelay_AreInSequenceContext()
    {
        var flow = BuildFlowOnPlayerTurn();

        // We'll check the pending context before it's consumed
        flow.Choose(BattleCommand.Attack, 0, new FixedRng());

        var ctx = flow.PendingSequenceContext;
        Assert.NotNull(ctx);
        Assert.Equal(BattleSide.Player, ctx.AttackerSide);
        Assert.Equal(10, ctx.AttackerId);
        Assert.Equal(20, ctx.TargetId);
    }

    [Fact]
    public void CameraOwnership_PhaseActionSequence_DoesNotTriggerActivate()
    {
        var flow = BuildFlowOnPlayerTurn();
        var script = ScriptWith((BattleActionOpcode.Wait, [5]), (BattleActionOpcode.End, []));
        var host = new SequenceTrackingHost(flow, new StubScriptProvider(script));
        var coordinator = new BattleTickCoordinator(host);

        flow.Choose(BattleCommand.Attack, 0, new FixedRng());

        // Tick during ActionSequence: ActivatePlayerCamera must NOT be called
        coordinator.Tick();
        coordinator.Tick();

        Assert.Equal(BattleFlowPhase.ActionSequence, flow.Phase);
        Assert.False(host.ActivateCalled);
    }

    [Fact]
    public void FinalKnockout_StillPlaysSequenceBeforeOutcome()
    {
        var flow = BuildFlowOnPlayerTurn();
        Assert.IsType<BattleFighter>(flow.State.ActiveEnemyFighter).Hp = 1;
        flow.PlayerBasicTech = new TechData { Script = 5, Accuracy = 100, Power = 100 };
        flow.Choose(BattleCommand.Attack, 0, new FixedRng());
        Assert.Equal(BattleOutcome.Won, flow.Outcome);
        Assert.Equal(BattleFlowPhase.ActionSequence, flow.Phase);
    }

    [Fact]
    public void PresentationHp_WaitsForSequenceCompletionNotHitRequest()
    {
        var flow = BuildFlowOnPlayerTurn();
        flow.PlayerBasicTech = new TechData { Script = 5, Accuracy = 100, Power = 100 };
        int before = Assert.IsType<BattleFighter>(flow.State.ActiveEnemyFighter).Hp;
        flow.Choose(BattleCommand.Attack, 0, new FixedRng());
        var view = BattleViewBuilder.Build(flow, new BattleMenuView(BattleMenuKind.Root, 0, 0, 6, 1, [], null, null, null), null, _ => BattleTextLine.Missing);
        Assert.Equal(before, view.Enemies[0].Hp);
        Assert.True(Assert.IsType<BattleFighter>(flow.State.Enemies[0]).Hp < before);
        flow.CompleteActionSequence();
        var after = BattleViewBuilder.Build(flow, new BattleMenuView(BattleMenuKind.Root, 0, 0, 6, 1, [], null, null, null), null, _ => BattleTextLine.Missing);
        Assert.Equal(Assert.IsType<BattleFighter>(flow.State.Enemies[0]).Hp, after.Enemies[0].Hp);
    }

    internal static BattleFlowController BuildFlowOnPlayerTurn()
    {
        var setup = new BattleSetup();

        // High player speed ensures the player gets the first turn regardless of RNG.
        setup.Partners[0] = new PartnerSeed
        {
            Id = 10,
            Hp = 100,
            MaxHp = 100,
            Mp = 100,
            MaxMp = 100,
            Stats = new BattleStats { Stats = [0, 0, 0, 0, 100] },
        };
        setup.Enemies[0] = new BattleEnemy { Fighter = 20, Hp = 100 };
        var state = BattleState.InitFighters(setup, digimon: 10, new FixedRng());
        var flow = new BattleFlowController
        {
            PlayerBasicTech = new TechData { Script = 5, Accuracy = 100 },
        };
        flow.Start(state);
        return flow;
    }

    private static BattleActionScript ScriptWith(params (BattleActionOpcode, short[])[] entries)
    {
        var commands = entries
            .Select(e => new BattleActionCommand(e.Item1, e.Item2))
            .ToList();
        return new BattleActionScript(5, commands);
    }

    internal sealed class SequenceTrackingHost : IBattleTickHost
    {
        private readonly BattleSequenceRunner? _runner;

        public SequenceTrackingHost(BattleFlowController flow, IActionScriptProvider? provider = null, IBattleSequencePlayback? playback = null)
        {
            Flow = flow;
            if (provider is not null)
            {
                _runner = new BattleSequenceRunner(flow, provider);
                if (playback is not null)
                {
                    _runner.SetPlayback(playback);
                }
            }
        }

        public BattleFlowController Flow { get; }

        public bool HasFightReport => false;

        public bool IsPlayerCameraActive { get; private set; }

        public bool IsBattleResolved => false;

        public bool ActivateCalled { get; private set; }

        public bool DeactivateCalled { get; private set; }

        public List<BattleSequenceEvent> CapturedEvents { get; } = [];

        public void ActivatePlayerCamera()
        {
            ActivateCalled = true;
            IsPlayerCameraActive = true;
        }

        public void DeactivatePlayerCamera()
        {
            DeactivateCalled = true;
            IsPlayerCameraActive = false;
        }

        public void TickBattleMenu()
        {
        }

        public void EnemyAct() => Flow.EnemyAct(new FixedRng());

        public void TickActionSequence() => _runner?.Tick();

        public void RefreshModels()
        {
        }

        public bool TickActorTransition() => false;

        public void ResolveOutcome()
        {
        }

        public void MarkBattleResolved()
        {
        }

        public bool AdvanceVictoryFade() => false;

        public void BeginFightReport()
        {
        }

        public void EndBattle()
        {
        }

        public void TickFightReport()
        {
        }
    }

    private sealed class StubScriptProvider : IActionScriptProvider
    {
        private readonly BattleActionScript? _script;

        public StubScriptProvider(BattleActionScript? script = null) => _script = script;

        public BattleActionScript? TryLoad(int fighterId, int scriptSelector) => _script;
    }

    private sealed class FixedRng : IBattleRng
    {
        public int NextValue() => 0;
    }
}
