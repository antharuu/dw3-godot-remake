using System.Text.Json;
using Dw3Production.Diagnostics.Field;
using Dw3Production.Logging;
using Dw3Production.Presentation;
using Godot;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Captures controlled real-fighter overlap through the unchanged production collector and GPU executor.</summary>
internal static class BattleRasterOverlapProof
{
    /// <summary>Captures the full streams, then optional independently selected primitive-prefix vectors.</summary>
    /// <param name="host">Live production host, with no disc access.</param>
    /// <param name="root">Private evidence directory.</param>
    /// <returns>A task completed after all configured real vectors have been exercised.</returns>
    public static async Task RunAsync(IBattleProbeHost host, string root)
    {
        var renderer = host.Renderer;
        var enemyPath = host.ResolveBattleModelPath(51);
        renderer.BuildModels(host.ResolveBattleModelPath(374), enemyPath, "fighter-374", "fighter-51", 51);
        renderer.SetProbePose(10);
        renderer.SetProbeAnimationFrame(10);
        renderer.SetCommandMenuCamera();
        var player = renderer.ModelForProbe(374);
        var opponent = renderer.ModelForProbe(51);
        opponent.Root.Position = player.Root.Position;
        renderer.PartnerForProbe.AddOpponentForProbe(enemyPath);
        var main = new BattleRasterControl();
        var partner = new BattleRasterControl();
        renderer.RasterForProbe.ProbeControl = main;
        renderer.PartnerForProbe.RasterForProbe.ProbeControl = partner;
        await CaptureAsync(host, root, "overlap-full");
        var depth = -(renderer.CameraForProbe.GlobalTransform.AffineInverse() * player.Root.GlobalPosition).Z;
        var rawDepth = -(renderer.PartnerForProbe.CameraForProbe.GlobalTransform.AffineInverse() * renderer.PartnerForProbe.RequiredModelForProbe.Root.GlobalPosition).Z;
        foreach (var configuration in new[] { "additive", "half" })
        {
            AlignActors(renderer, configuration, depth, rawDepth);
            var directory = Path.Combine(root, configuration);
            Directory.CreateDirectory(directory);
            await CaptureAsync(host, directory, "overlap-full");
            BattleRasterEvidence.WriteScene(renderer, directory);
            BattleRasterEvidence.Write(main, directory, "main");
            BattleRasterEvidence.Write(partner, directory, "raw");
            QuietLog.Info($"battle-overlap setup={configuration} frame=10 source-camera=unchanged player={player.Root.Position} opponent={opponent.Root.Position} main-primitives={main.Ordered.Count} raw-primitives={partner.Ordered.Count}");
            await ReplayAsync(host, directory, main, partner);
        }

        renderer.RasterForProbe.ProbeControl = null;
        renderer.PartnerForProbe.RasterForProbe.ProbeControl = null;
    }

    /// <summary>Aligns observed ABR-bearing bones using diagnostic actor translation only.</summary>
    /// <param name="renderer">Production renderer with unchanged cameras.</param>
    /// <param name="configuration">Which fighter must be nearer in this controlled overlap.</param>
    /// <param name="depth">Original main player depth, preserving its projection scale.</param>
    /// <param name="rawDepth">Original partner player depth, preserving its projection scale.</param>
    private static void AlignActors(BattleRenderer renderer, string configuration, float depth, float rawDepth)
    {
        var offset = configuration == "additive" ? 1f : -1f;
        BattleRasterAlignment.Align(renderer.CameraForProbe, renderer.ModelForProbe(374), 3, new Vector2(640, 450), depth);
        BattleRasterAlignment.Align(renderer.CameraForProbe, renderer.ModelForProbe(51), 2, new Vector2(640, 450), depth + offset);
        var partner = renderer.PartnerForProbe;
        BattleRasterAlignment.Align(partner.CameraForProbe, partner.RequiredModelForProbe, 3, new Vector2(50, 30), rawDepth);
        BattleRasterAlignment.Align(partner.CameraForProbe, partner.OpponentForProbe, 2, new Vector2(50, 30), rawDepth + offset);
    }

    /// <summary>Replays external vector coordinates selected independently from recorded projected geometry and raw alpha.</summary>
    /// <param name="host">Live production host.</param>
    /// <param name="root">Capture directory.</param>
    /// <param name="main">Main-view command intervention.</param>
    /// <param name="partner">Partner-view command intervention.</param>
    /// <returns>A task completed after every prefix and wrong-blend capture.</returns>
    private static async Task ReplayAsync(IBattleProbeHost host, string root, BattleRasterControl main, BattleRasterControl partner)
    {
        var path = System.Environment.GetEnvironmentVariable("DW3_BATTLE_STP_VECTORS");
        if (string.IsNullOrWhiteSpace(path))
        {
            ProbeRequirement.Require(System.Environment.GetEnvironmentVariable("DW3_BATTLE_STP_DISCOVERY") == "1", "real STP proof requires independently selected vectors, discovery is not a delivered proof");
            QuietLog.Info("battle-overlap discovery-only: no sample vectors supplied, not a blend proof PASS");
            return;
        }

        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        BattleRasterVectorCorpus.Validate(document.RootElement);
        foreach (var vector in document.RootElement.EnumerateArray().Where(item => item.GetProperty("configuration").GetString() == Path.GetFileName(root)))
        {
            var name = vector.GetProperty("name").GetString() ?? throw new InvalidOperationException("vector name missing");
            var control = vector.GetProperty("view").GetString() == "main" ? main : partner;
            var index = vector.GetProperty("index").GetInt32();
            control.Limit = index;
            await CaptureAsync(host, root, $"{name}-destination");
            control.HiddenFighter = vector.GetProperty("opponent").GetInt32();
            await CaptureAsync(host, root, $"{name}-no-opponent");
            control.HiddenFighter = -1;
            control.Limit = index + 1;
            control.WrongBlend = System.Environment.GetEnvironmentVariable("DW3_BATTLE_PROOF_WRONG_BLEND") == "1";
            await CaptureAsync(host, root, $"{name}-actual");
            control.WrongBlend = true;
            await CaptureAsync(host, root, $"{name}-wrong");
            control.WrongBlend = false;
            BattleRasterSampleOracle.Verify(vector, control.Ordered[index], root);
            control.Limit = int.MaxValue;
        }
    }

    /// <summary>Captures the composed main framebuffer and the independent raw 100x60 target at the same pose.</summary>
    /// <param name="host">Production host.</param>
    /// <param name="root">Private output directory.</param>
    /// <param name="name">Stable pair name.</param>
    /// <returns>A task completed once both image files exist.</returns>
    private static async Task CaptureAsync(IBattleProbeHost host, string root, string name)
    {
        using var image = await BattleProbeCapture.CaptureAsync(host, root, $"{name}-main.png");
        ProbeRequirement.Require(host.Renderer.SavePartnerView(Path.Combine(root, $"{name}-raw.png")) == Error.Ok, "raw overlap target capture failed");
    }
}
