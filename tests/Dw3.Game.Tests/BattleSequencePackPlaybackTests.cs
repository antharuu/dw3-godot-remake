using Dw3.Data;
using Dw3.Data.Battle;
using Dw3.Extractor;
using Dw3Production;
using Dw3Production.Conversion;
using Dw3Production.Simulation;
using Microsoft.Data.Sqlite;
using Xunit;
using TechData = Dw3Production.Simulation.TechData;

namespace Dw3.Game.Tests;

public sealed class BattleSequencePackPlaybackTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"dw3-playback-{Guid.NewGuid():N}");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProductionPackWaitsForMotionAcknowledgementAndChildren(bool reserve)
    {
        WritePack();
        using var loader = new BattleDataPackLoader(new DataPackReader(_root));
        var flow = BattleFlowActionSequenceTests.BuildFlowOnPlayerTurn();
        var enemy = Assert.IsType<BattleFighter>(flow.State.Enemies[0]);
        enemy.Hp = 1;
        if (reserve)
        {
            flow.State.Enemies[1] = new BattleFighter { Id = 21, Hp = 100, MaxHp = 100 };
        }

        flow.PlayerBasicTech = new TechData { Script = 5, Accuracy = 100, Power = 100 };
        var backend = new SequenceTestPlayback();
        var host = new BattleFlowActionSequenceTests.SequenceTrackingHost(flow, new PackActionScriptProvider(loader), backend);
        var clock = new BattleTickCoordinator(host);
        var rng = new CountingRng();
        Assert.True(flow.Choose(BattleCommand.Attack, 0, rng));
        int draws = rng.Draws;
        clock.Tick();
        Assert.Equal(300, backend.Camera.Projection);
        Assert.Single(backend.Applied.OfType<ModelMotionEvent>());
        Assert.Empty(backend.Applied.OfType<HitDispatchedEvent>());
        clock.Tick();
        Assert.Equal(1, PresentedEnemyHp(flow));
        backend.Ready = true;
        clock.Tick();
        Assert.Empty(backend.Applied.OfType<HitDispatchedEvent>());
        Assert.Equal(BattleFlowPhase.ActionSequence, flow.Phase);
        backend.AcceptAssets = true;
        clock.Tick();
        var hit = Assert.Single(backend.Applied.OfType<HitDispatchedEvent>());
        Assert.Equal(20, hit.TargetId);
        Assert.Equal(10, hit.AttackerId);
        Assert.Equal(1, PresentedEnemyHp(flow));
        Assert.Equal(draws, rng.Draws);
        Assert.False(backend.ChildrenReady);
        clock.Tick();
        Assert.Single(backend.Applied.OfType<HitDispatchedEvent>());
        backend.ChildrenReady = true;
        clock.Tick();
        Assert.NotEqual(BattleFlowPhase.ActionSequence, flow.Phase);
        Assert.Equal(280, backend.Camera.Projection);
        Assert.Equal(1, backend.Finishes);
        Assert.Equal(0, PresentedEnemyHp(flow));
        Assert.Equal(reserve ? BattleOutcome.None : BattleOutcome.Won, flow.Outcome);
    }

    [Fact]
    public void PhysicalMultiHitTechnique_UsesItsDataSelectedScriptAndFrozenTarget()
    {
        WritePack();
        using var loader = new BattleDataPackLoader(new DataPackReader(_root));
        var flow = BattleFlowActionSequenceTests.BuildFlowOnPlayerTurn();
        var technique = new TechData
        {
            Accuracy = 100,
            Effect = BattleTechniqueEffect.MultiHit,
            HitCount = 4,
            Power = 1,
            Script = 5,
        };
        flow.PlayerTechniques = [new BattleTechnique(289, "physical-multi", technique)];
        var backend = new SequenceTestPlayback { Ready = true, AcceptAssets = true, ChildrenReady = true };
        var runner = new BattleSequenceRunner(flow, new PackActionScriptProvider(loader));
        runner.SetPlayback(backend);

        Assert.True(flow.Choose(BattleCommand.Technique, 289, new CountingRng()));
        ActionSequenceContext context = Assert.IsType<ActionSequenceContext>(flow.PendingSequenceContext);
        Assert.Equal(10, context.AttackerId);
        Assert.Equal(20, context.TargetId);
        Assert.Equal(5, context.ScriptSelector);
        Assert.Equal(4, context.HitEvents.Count);
        Assert.All(context.HitEvents, hit => Assert.Equal(BattleTechniqueEffect.MultiHit, hit.Effect));

        for (int tick = 0; tick < 10 && flow.Phase == BattleFlowPhase.ActionSequence; tick++)
        {
            runner.Tick();
        }

        Assert.Null(runner.Failure);
        Assert.NotEqual(BattleFlowPhase.ActionSequence, flow.Phase);
        var dispatch = Assert.Single(backend.Applied.OfType<HitDispatchedEvent>());
        Assert.Equal(20, dispatch.TargetId);
        Assert.Equal(4, dispatch.HitEvents.Count);
    }

    [Fact]
    public void CancelAndReentryDoNotPublishOrphanedEvents()
    {
        WritePack();
        using var loader = new BattleDataPackLoader(new DataPackReader(_root));
        var flow = BattleFlowActionSequenceTests.BuildFlowOnPlayerTurn();
        var backend = new SequenceTestPlayback();
        var runner = new BattleSequenceRunner(flow, new PackActionScriptProvider(loader));
        runner.SetPlayback(backend);
        flow.Choose(BattleCommand.Attack, 0, new CountingRng());
        runner.Tick();
        flow.EndBattle(BattleOutcome.Lost);
        runner.Tick();
        backend.Ready = true;
        backend.AcceptAssets = true;
        runner.Tick();
        Assert.Empty(backend.Applied.OfType<HitDispatchedEvent>());
        Assert.Equal(1, backend.Finishes);
        Assert.Equal(280, backend.Camera.Projection);
        flow.Start(BattleFlowActionSequenceTests.BuildFlowOnPlayerTurn().State);
        runner.Tick();
        Assert.Empty(backend.Applied.OfType<HitDispatchedEvent>());
    }

    [Fact]
    public void MissingRequiredResourceIsRecoverableNotSuccessful()
    {
        WritePack();
        using var loader = new BattleDataPackLoader(new DataPackReader(_root));
        var flow = BattleFlowActionSequenceTests.BuildFlowOnPlayerTurn();
        flow.PlayerBasicTech = new TechData { Script = 6, Accuracy = 100 };
        var runner = new BattleSequenceRunner(flow, new PackActionScriptProvider(loader));
        flow.Choose(BattleCommand.Attack, 0, new CountingRng());
        runner.Tick();
        Assert.Contains("required-unavailable", runner.Failure, StringComparison.Ordinal);
        Assert.NotNull(flow.PendingSequenceContext);
        Assert.Equal(BattleFlowPhase.ActionSequence, flow.Phase);
    }

    [Fact]
    public void RestoredResourceResumesTheSameResolvedAction()
    {
        WritePack();
        using var loader = new BattleDataPackLoader(new DataPackReader(_root));
        string path = loader.ActionScriptPath(10, 5);
        string content = File.ReadAllText(path);
        File.Delete(path);
        var flow = BattleFlowActionSequenceTests.BuildFlowOnPlayerTurn();
        var backend = new SequenceTestPlayback { Ready = true, AcceptAssets = true, ChildrenReady = true };
        var runner = new BattleSequenceRunner(flow, new PackActionScriptProvider(loader));
        runner.SetPlayback(backend);
        var rng = new CountingRng();
        flow.Choose(BattleCommand.Attack, 0, rng);
        int draws = rng.Draws;
        runner.Tick();
        runner.Tick();
        Assert.NotNull(runner.Failure);
        Assert.Empty(backend.Applied);
        File.WriteAllText(path, content);
        runner.Tick();
        Assert.Null(runner.Failure);
        Assert.Equal(draws, rng.Draws);
        Assert.Single(backend.Applied.OfType<HitDispatchedEvent>());
        Assert.NotEqual(BattleFlowPhase.ActionSequence, flow.Phase);
    }

    [Fact]
    public void UnavailablePhysicalResourceIsDiagnosedAndCanRetry()
    {
        WritePack();
        using var loader = new BattleDataPackLoader(new DataPackReader(_root));
        var flow = BattleFlowActionSequenceTests.BuildFlowOnPlayerTurn();
        var backend = new SequenceTestPlayback { MissingCamera = true, Ready = true, AcceptAssets = true, ChildrenReady = true };
        var runner = new BattleSequenceRunner(flow, new PackActionScriptProvider(loader));
        runner.SetPlayback(backend);
        flow.Choose(BattleCommand.Attack, 0, new CountingRng());
        runner.Tick();
        Assert.Contains("camera resource unavailable", runner.Failure, StringComparison.Ordinal);
        Assert.Equal(BattleFlowPhase.ActionSequence, flow.Phase);
        Assert.Empty(backend.Applied);
        backend.MissingCamera = false;
        runner.Tick();
        Assert.Null(runner.Failure);
        Assert.Single(backend.Applied.OfType<CameraRequestEvent>());
        Assert.NotEqual(BattleFlowPhase.ActionSequence, flow.Phase);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, true);
        }
    }

    private static int PresentedEnemyHp(BattleFlowController flow) => BattleViewBuilder.Build(flow, new BattleMenuView(BattleMenuKind.Root, 0, 0, 6, 1, [], null, null, null), null, _ => BattleTextLine.Missing).Enemies[0].Hp;

    private void WritePack() => new DataPackWriter(_root).Write(
        new DataPackMetadata("test", "synthetic", SyntheticDataPackFixture.SyntheticEdition, DateTimeOffset.UnixEpoch),
        context => BattleActionDomainWriter.Write(
            context,
            [
                new BattleActionExtraction(
                10,
                1,
                0,
                5,
                new BattleActionScript(
                    5,
                    [
                        new(BattleActionOpcode.Camera, [0, 1, 256, 512, 768, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 300]),
                        new(BattleActionOpcode.Model, [0, 0, 7]),
                        new(BattleActionOpcode.Model, [0, 0, 0]),
                        new(BattleActionOpcode.HitDispatch, [0]),
                        new(BattleActionOpcode.End, []),
                    ]),
                null),
            ]));

    private sealed class CountingRng : IBattleRng
    {
        public int Draws { get; private set; }

        public int NextValue()
        {
            Draws++;
            return 0;
        }
    }
}
