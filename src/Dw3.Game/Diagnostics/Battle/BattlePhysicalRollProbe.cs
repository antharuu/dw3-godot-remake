using Dw3.Data;
using Dw3.Data.Battle;
using Dw3Production.Conversion;
using Dw3Production.Diagnostics.Field;
using Dw3Production.Simulation;
using Godot;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Discriminates authored view twist from an unrolled view on the actual camera consumer.</summary>
internal sealed class BattlePhysicalRollProbe : BattleProbe
{
    /// <summary>Initializes a new instance of the <see cref="BattlePhysicalRollProbe"/> class.</summary>
    /// <param name="host">Offscreen production host.</param>
    public BattlePhysicalRollProbe(IBattleProbeHost host)
        : base(host)
    {
    }

    /// <inheritdoc/>
    protected override string FailureCode => "B9053_ROLL";

    /// <inheritdoc/>
    protected override async Task ExecuteAsync()
    {
        Host.ApplyS200();
        using var loader = new BattleDataPackLoader(new DataPackReader(System.Environment.GetEnvironmentVariable("DW3_DATA_DIR") ?? throw new InvalidOperationException("Explicit pack required.")));
        var provider = new PackActionScriptProvider(loader);
        string root = Host.PrepareCaptureDirectory("probe/battle-roll");
        foreach (var pair in new[] { (68, 20), (71, 234), (92, 214), (92, 447) })
        {
            Host.StartDebugBattle();
            await Host.NextFrameDrawn();
            Host.Renderer.BuildModels(loader.ModelPath(pair.Item2), loader.ModelPath(51), "roll proof", "target", 51);
            var player = Host.Flow.State.ActivePlayerFighter ?? throw new InvalidOperationException("No active player.");
            player.Id = pair.Item2;
            Host.AdoptSheetFighter(pair.Item2);
            await BattleProbeCapture.CaptureAsync(Host, root, $"{pair.Item1}-{pair.Item2}-ready.png");
            var script = provider.TryLoad(pair.Item2, 11) ?? throw new InvalidOperationException("Source provider rejected camera script.");
            BattleActionValidation.Validate(script);
            GD.Print($"B9053_ROLL_VALIDATED {pair}");

            foreach (var command in script.Commands.Where(command => command.Opcode == BattleActionOpcode.Camera && command.Arguments.Count == 16 && command.Arguments[14] != 0))
            {
                await VerifyAsync(command, pair, root);
            }
        }

        GD.Print("B9053_ROLL_PASS physical-camera control/cut/fade/end/cancel ownership");
    }

    /// <summary>Uses the extracted operands, with a single zero-roll negative control.</summary>
    /// <param name="command">Unmodified extracted camera command.</param>
    /// <param name="pair">Technique and natural owner.</param>
    /// <param name="root">Private capture directory.</param>
    /// <returns>Completion after both physical restoration paths.</returns>
    private async Task VerifyAsync(BattleActionCommand command, (int Technique, int Fighter) pair, string root)
    {
        var renderer = Host.Renderer;
        var consumer = renderer.SequencePlayback;
        renderer.SetOpeningCamera();
        var home = renderer.CameraForProbe.Transform;
        var context = new ActionSequenceContext(BattleSide.Player, pair.Fighter, 51, [], 11);
        short[] zero = [.. command.Arguments];
        zero[14] = 0;
        consumer.Begin(context);
        consumer.Consume(new CameraRequestEvent(BattleSide.Player, zero));
        renderer.StepCamera(10);
        var unrolled = renderer.CameraForProbe.Transform;
        for (int sample = 0; sample < 2000; sample++)
        {
            renderer.StepCamera(0);
        }

        ProbeRequirement.Require(renderer.CameraForProbe.Transform.IsEqualApprox(unrolled), "repeated camera application changed its basis");
        renderer.RasterForProbe.Rebuild(renderer.CameraForProbe, consumer.DiagnosticRoster.Models);
        await BattleProbeCapture.CaptureRawAsync(Host, root, $"{pair.Technique}-{pair.Fighter}-zero.png");
        consumer.Finish(true);
        ProbeRequirement.Require(renderer.CameraForProbe.Transform.IsEqualApprox(home), "zero control restoration");
        foreach (bool cancelled in new[] { false, true })
        {
            consumer.Begin(context);
            consumer.Consume(new CameraRequestEvent(BattleSide.Player, command.Arguments));
            renderer.StepCamera(10);
            var rolled = renderer.CameraForProbe.Transform;
            float angle = command.Arguments[14] * Mathf.Tau / 4096f;
            var expected = unrolled.Basis * new Basis(Vector3.Back, -angle);
            GD.Print($"B9053_ROLL_STATE {pair} rz={command.Arguments[14]} zero={unrolled} actual={rolled} expected={expected}");
            ProbeRequirement.Require(!rolled.Basis.IsEqualApprox(unrolled.Basis), "nonzero roll ignored by physical consumer");
            ProbeRequirement.Require(rolled.Basis.IsEqualApprox(expected), "view twist composition differs");
            ProbeRequirement.Require(rolled.Origin.IsEqualApprox(unrolled.Origin), "view twist moved the eye");
            renderer.SetCommandMenuCamera();
            renderer.StopShotCamera();
            ProbeRequirement.Require(renderer.CameraForProbe.Transform.IsEqualApprox(rolled), "menu overwrote sequence roll");
            renderer.RasterForProbe.Rebuild(renderer.CameraForProbe, consumer.DiagnosticRoster.Models);
            await BattleProbeCapture.CaptureRawAsync(Host, root, $"{pair.Technique}-{pair.Fighter}-roll-{cancelled}.png");
            consumer.Finish(cancelled);
            ProbeRequirement.Require(renderer.CameraForProbe.Transform.IsEqualApprox(home), "roll restoration failed");
            renderer.RasterForProbe.Rebuild(renderer.CameraForProbe, consumer.DiagnosticRoster.Models);
            await BattleProbeCapture.CaptureRawAsync(Host, root, $"{pair.Technique}-{pair.Fighter}-return-{cancelled}.png");
            GD.Print($"B9053_ROLL_RETURN {pair} cancelled={cancelled} exact=true");
        }
    }
}
