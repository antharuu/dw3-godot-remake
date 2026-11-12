using System.Text.Json;
using Dw3.Data;
using Dw3.Data.Battle;
using Dw3Production.Conversion;
using Dw3Production.Diagnostics.Field;
using Dw3Production.Simulation;
using Godot;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Exercises the physical model/camera slices of DNA scripts without claiming their excluded effect backends.</summary>
internal sealed class BattlePhysicalActorProbe : BattleProbe
{
    /// <summary>Initializes a new instance of the <see cref="BattlePhysicalActorProbe"/> class.</summary>
    /// <param name="host">Offscreen production host.</param>
    public BattlePhysicalActorProbe(IBattleProbeHost host)
        : base(host)
    {
    }

    /// <inheritdoc/>
    protected override string FailureCode => "B9053_ACTORS";

    /// <inheritdoc/>
    protected override async Task ExecuteAsync()
    {
        Host.ApplyS200();
        using var loader = new BattleDataPackLoader(new DataPackReader(System.Environment.GetEnvironmentVariable("DW3_DATA_DIR") ?? throw new InvalidOperationException("Explicit pack required.")));
        var provider = new PackActionScriptProvider(loader);
        string root = Host.PrepareCaptureDirectory("probe/battle-actors");
        BattlePhysicalRegistry.Write(loader, root);
        foreach (var tech in loader.LoadTechs().Techs.Values.Where(tech => tech.Id is 374 or 376 or 377 or 378 or 380))
        {
            foreach (int owner in loader.LoadFighters().Digimon.Values.Where(owner => owner.PairTech == tech.Id).Select(owner => owner.Id))
            {
                if (System.Environment.GetEnvironmentVariable("DW3_PHYSICAL_ACTOR_OWNER") is { Length: > 0 } selected && owner.ToString(System.Globalization.CultureInfo.InvariantCulture) != selected)
                {
                    continue;
                }

                var script = provider.TryLoad(owner, tech.Script) ?? throw new InvalidOperationException("Real DNA script unavailable.");
                BattleActionValidation.Validate(script);
                foreach (bool cancelled in new[] { false, true })
                {
                    await RunCaseAsync(loader, root, (tech.Id, owner), script, cancelled);
                }
            }
        }

        GD.Print("B9053_ACTORS_PASS mechanism-slices-only full-techniques-NOT-rendered effects/fades/audio-excluded");
    }

    /// <summary>Loads the real natural owner and constructs a source-order physical slice with explicit exclusions.</summary>
    /// <param name="loader">Validated real pack.</param>
    /// <param name="root">Private capture root.</param>
    /// <param name="pair">Technique and natural owner.</param>
    /// <param name="source">Complete unmodified provider script.</param>
    /// <param name="cancelled">Whether to interrupt after the fused actor becomes visible.</param>
    /// <returns>Completion after physical cleanup assertions.</returns>
    private async Task RunCaseAsync(BattleDataPackLoader loader, string root, (int Tech, int Owner) pair, BattleActionScript source, bool cancelled)
    {
        Host.StartDebugBattle();
        await Host.NextFrameDrawn();
        var renderer = Host.Renderer;
        renderer.BuildModels(loader.ModelPath(pair.Owner), loader.ModelPath(51), "DNA owner", "target", 51);
        Host.AdoptSheetFighter(pair.Owner);
        renderer.ConfigureActionAssets(loader);
        renderer.StopShotCamera();
        renderer.SetOpeningCamera();
        var homes = renderer.SequencePlayback.DiagnosticRoster.Models.ToDictionary(model => model, model => model.Root.Transform);
        var camera = renderer.CameraForProbe.Transform;
        var context = new ActionSequenceContext(BattleSide.Player, pair.Owner, 51, [], source.Entry);
        var kept = source.Commands.Select((command, index) => (command, index)).Where(item => item.command.Opcode is BattleActionOpcode.Model or BattleActionOpcode.Camera or BattleActionOpcode.Wait or BattleActionOpcode.End or BattleActionOpcode.AlternateEnd).ToArray();
        var slice = new BattleActionScript(source.Entry, [.. kept.Select(item => item.command)]);
        GD.Print($"B9053_ACTORS_SOURCE {pair} cancelled={cancelled} kept={JsonSerializer.Serialize(kept.Select(item => item.index))} source={JsonSerializer.Serialize(source)}");
        var consumer = renderer.SequencePlayback;
        consumer.Begin(context);
        var timeline = new BattleActionTimeline(slice, context) { Acknowledge = request => consumer.Consume(request) == SequenceAcknowledgement.Applied, MotionReady = consumer.MotionReady, JumpReady = consumer.JumpReady, ChildrenReady = () => consumer.ChildrenReady };
        string captures = Path.Combine(root, $"{pair.Tech}-{pair.Owner}-{cancelled}");
        Directory.CreateDirectory(captures);
        float turn = source.Commands.First(command => command.Opcode == BattleActionOpcode.Model && command.Arguments is [4, 2 or 3, _, not 0, _, _]).Arguments[3] * Mathf.Tau / 4096f;
        await PlayAsync(timeline, captures, cancelled, turn);
        consumer.Finish(cancelled);
        ProbeRequirement.Require(homes.Count == consumer.DiagnosticRoster.Models.Count && homes.All(item => item.Key.Root.Transform.IsEqualApprox(item.Value)), "actor cleanup replaced or moved combatants");
        ProbeRequirement.Require(renderer.CameraForProbe.Transform.IsEqualApprox(camera) && !consumer.OwnsCamera, "actor cleanup leaked camera ownership");
        renderer.Present(Host.BuildBattleView());
        await BattleProbeCapture.CaptureRawAsync(Host, captures, "returned.png");
        GD.Print($"B9053_ACTORS_RETURN {pair} cancelled={cancelled} same-combatants=exact camera=exact count={consumer.DiagnosticRoster.Models.Count}");
    }

    /// <summary>Advances actual model consumers with normalized time and captures each visible additional fighter.</summary>
    /// <param name="timeline">Source-order model/camera slice.</param>
    /// <param name="captures">Private case directory.</param>
    /// <param name="cancelled">Whether to interrupt the fusion display.</param>
    /// <param name="turn">Authored nonzero rotation to observe on a real fighter root.</param>
    /// <returns>Completion after End or the intended cancellation point.</returns>
    private async Task PlayAsync(BattleActionTimeline timeline, string captures, bool cancelled, float turn)
    {
        var renderer = Host.Renderer;
        var roster = renderer.SequencePlayback.DiagnosticRoster;
        var seen = new HashSet<int>();
        var visibleTicks = new Dictionary<int, int>();
        var rotations = new Dictionary<Dw3Production.Presentation.BattleModel, float>();
        bool rotated = false;
        var view = Host.BuildBattleView() ?? throw new InvalidOperationException("No battle view.");
        view = view with { Phase = BattleFlowPhase.ActionSequence };
        for (int tick = 0; tick < 5000 && !timeline.IsComplete; tick++)
        {
            timeline.Advance();
            renderer.Present(view, 1d / 60d);
            rotated |= ObserveRotation(rotations, turn);
            foreach (var model in roster.Models.Skip(2).Where(model => model.Root.Visible && !seen.Contains(model.FighterId)))
            {
                int samples = visibleTicks.GetValueOrDefault(model.FighterId) + 1;
                visibleTicks[model.FighterId] = samples;
                if (samples < 30)
                {
                    continue;
                }

                seen.Add(model.FighterId);
                await CaptureActorAsync(model, captures);
                GD.Print($"B9053_ACTOR_VISIBLE fighter={model.FighterId} transform={model.Root.Transform} motion={model.Motion?.Motion}");
            }

            if (cancelled && seen.Count == 2)
            {
                timeline.Cancel();
                break;
            }

            if (tick % 200 == 0)
            {
                await Host.NextFrameDrawn();
            }
        }

        ProbeRequirement.Require(timeline.IsComplete, $"actor slice stalled cursor={timeline.Cursor}");
        ProbeRequirement.Require(seen.Count == 2, $"additional fighters not physically displayed: {seen.Count}");
        ProbeRequirement.Require(rotated, "authored Model4 rotation never reached the real actor root");
        GD.Print($"B9053_ACTOR_ROTATION expected={turn} real-root-reached={rotated}");
    }

    /// <summary>Discriminates actual root rotation from acknowledging a decoded rotation command.</summary>
    /// <param name="rotations">Initial root angles captured immediately after each spawn.</param>
    /// <param name="turn">Authored turn relative to the spawned home orientation.</param>
    /// <returns>Whether a real model reached the requested nonzero angle.</returns>
    private bool ObserveRotation(Dictionary<Dw3Production.Presentation.BattleModel, float> rotations, float turn)
    {
        foreach (var model in Host.Renderer.SequencePlayback.DiagnosticRoster.Models.Skip(2))
        {
            rotations.TryAdd(model, model.Root.Rotation.Y);
            float actual = Mathf.AngleDifference(rotations[model], model.Root.Rotation.Y);
            if (Mathf.IsZeroApprox(Mathf.AngleDifference(turn, actual)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Requires the displayed model to contribute pixels, not merely hold a visible root flag.</summary>
    /// <param name="model">Actual additional fighter.</param>
    /// <param name="root">Private case captures.</param>
    /// <returns>Completion after the shown/hidden differential has been captured and the model restored.</returns>
    private async Task CaptureActorAsync(Dw3Production.Presentation.BattleModel model, string root)
    {
        var image = await BattleProbeCapture.CaptureRawAsync(Host, root, $"actor-{model.FighterId}.png");
        model.Root.Visible = false;
        Host.Renderer.RasterForProbe.Rebuild(Host.Renderer.CameraForProbe, Host.Renderer.SequencePlayback.DiagnosticRoster.Models);
        var hidden = await BattleProbeCapture.CaptureRawAsync(Host, root, $"actor-{model.FighterId}-hidden.png");
        model.Root.Visible = true;
        Host.Renderer.RasterForProbe.Rebuild(Host.Renderer.CameraForProbe, Host.Renderer.SequencePlayback.DiagnosticRoster.Models);
        int changed = BattlePixelMetrics.CountDifferent(image, hidden, 0, image.GetWidth());
        ProbeRequirement.Require(changed > 200, $"additional actor contributes no useful pixels: {model.FighterId}/{changed}");
        GD.Print($"B9053_ACTOR_PIXELS fighter={model.FighterId} changed={changed}");
    }
}
