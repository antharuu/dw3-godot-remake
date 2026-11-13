using Dw3Production.Conversion;
using Dw3Production.Diagnostics.Field;
using Dw3Production.Presentation;
using Dw3Production.Simulation;
using Godot;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Interrupts the real technique runner after it has physically displayed its optional arena.</summary>
internal static class BattlePhysicalStageCancellation
{
    /// <summary>Exercises cancellation and repeated entry without substituting a script or acknowledging missing assets.</summary>
    /// <param name="host">Offscreen production battle.</param>
    /// <param name="loader">Validated real pack.</param>
    /// <param name="rng">Already resolved simulation stream.</param>
    /// <param name="root">Private capture directory.</param>
    /// <returns>Completion after the actual scene is restored.</returns>
    public static async Task RunAsync(IBattleProbeHost host, BattleDataPackLoader loader, BattleAttackProofRng rng, string root)
    {
        var context = host.Flow.PendingSequenceContext ?? throw new InvalidOperationException("No frozen action.");
        var renderer = host.Renderer;
        renderer.StopShotCamera();
        renderer.SetOpeningCamera();
        var arena = renderer.GetSequenceStage();
        var camera = renderer.CameraForProbe.Transform;
        var homes = renderer.SequencePlayback.DiagnosticRoster.Models.ToDictionary(model => model, model => model.Root.Transform);
        var state = host.Flow.State.Players.Concat(host.Flow.State.Enemies).OfType<BattleFighter>().Select(fighter => (fighter, fighter.Hp, fighter.Mp)).ToArray();
        int draws = rng.Count;
        var runner = new BattleSequenceRunner(host.Flow, new PackActionScriptProvider(loader));
        runner.SetRenderer(renderer);
        for (int tick = 0; tick < 240 && !ReadyForCancellation(renderer, context); tick++)
        {
            runner.Tick();
            renderer.Present(host.BuildBattleView());
            ProbeRequirement.Require(runner.Failure is null, $"stage cancellation source failed: {runner.Failure}");
            await host.NextFrameDrawn();
        }

        ProbeRequirement.Require(ReadyForCancellation(renderer, context), "physical child never became active");
        var effects = renderer.SequencePlayback.DiagnosticEffectModels.Select(model => model.Root).ToArray();
        await BattleProbeCapture.CaptureRawAsync(host, root, effects.Length > 0 ? "effect-before-cancel.png" : "optional-before-cancel.png");
        runner.Cancel();
        ProbeRequirement.Require(ReferenceEquals(arena, renderer.GetSequenceStage()), "cancellation rebuilt or lost encounter arena");
        ProbeRequirement.Require(renderer.CameraForProbe.Transform.IsEqualApprox(camera), "cancelled camera not restored");
        ProbeRequirement.Require(homes.All(pair => pair.Key.Root.Transform.IsEqualApprox(pair.Value)), "cancelled fighters not restored");
        ProbeRequirement.Require(state.All(item => item.fighter.Hp == item.Hp && item.fighter.Mp == item.Mp), "cancellation reran rules");
        ProbeRequirement.Require(rng.Count == draws && !renderer.SequencePlayback.OwnsCamera && renderer.SequencePlayback.DiagnosticStage == 0, "cancelled owner or RNG leaked");
        ProbeRequirement.Require(renderer.SequencePlayback.DiagnosticEffectModelCount == 0 && renderer.SequencePlayback.DiagnosticLastEffectClearHadHiddenRoots && effects.All(root => root.IsQueuedForDeletion()), "cancelled effect root leaked");
        renderer.Present(host.BuildBattleView());
        await host.NextFrameDrawn();
        ProbeRequirement.Require(effects.All(root => !GodotObject.IsInstanceValid(root)), "cancelled effect root survived production frame");
        await BattleProbeCapture.CaptureRawAsync(host, root, "optional-after-cancel.png");
        GD.Print($"B9053_STAGE_CANCEL stage={context.ScriptStage} effects={effects.Length} arena=same-instance camera=exact fighters=exact rng={draws}/{rng.Count}");
    }

    /// <summary>Waits for the optional arena when authored, otherwise for an instantiated effect model.</summary>
    /// <param name="renderer">Production renderer owning the action children.</param>
    /// <param name="context">Frozen action context identifying the optional stage.</param>
    /// <returns>Whether cancellation would exercise a real physical child.</returns>
    private static bool ReadyForCancellation(BattleRenderer renderer, ActionSequenceContext context) =>
        context.ScriptStage >= 0
            ? renderer.SequencePlayback.DiagnosticStage == context.ScriptStage
            : renderer.SequencePlayback.DiagnosticEffectModelCount > 0;
}
