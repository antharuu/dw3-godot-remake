using Dw3Production.Diagnostics.Field;
using Dw3Production.Logging;
using Dw3Production.Presentation;
using Dw3Production.Simulation;
using Godot;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Checks local actors and actual overlay contributions in the production framebuffer.</summary>
internal sealed class BattleEntranceOracle
{
    private readonly IBattleProbeHost _host;
    private readonly string _root;
    private readonly Dictionary<int, double> _previous = [];

    private double _previousStage;

    /// <summary>Initializes a new instance of the <see cref="BattleEntranceOracle"/> class.</summary>
    /// <param name="host">Running battle.</param>
    /// <param name="root">Private capture directory.</param>
    public BattleEntranceOracle(IBattleProbeHost host, string root)
    {
        _host = host;
        _root = root;
    }

    /// <summary>Gets both real actors, rejecting stand-ins through the required model lookup.</summary>
    private BattleModel[] Actors =>
    [
        _host.Renderer.ModelForProbe(_host.Flow.State.ActivePlayerFighter!.Id),
        _host.Renderer.ModelForProbe(_host.Flow.State.ActiveEnemyFighter!.Id),
    ];

    /// <summary>Captures the next rendered frame without presenting or settling the fade.</summary>
    /// <param name="name">Private PNG filename.</param>
    /// <returns>The actual root viewport framebuffer.</returns>
    public Task<Image> CaptureAsync(string name) => BattleProbeCapture.CaptureRawAsync(_host, _root, name);

    /// <summary>Rejects local bright actors even when black margins dilute the whole-image mean.</summary>
    /// <param name="image">Unmodified first rendered frame.</param>
    public void RequireBlack(Image image)
    {
        foreach (var actor in Actors)
        {
            var region = ActorRegion(actor);
            var bright = BattleEntrancePixels.CountBright(image.GetData(), image.GetWidth(), image.GetFormat() == Image.Format.Rgba8 ? 4 : 3, region);
            var color = actor.EnumeratePolygons().SelectMany(polygon => polygon.Colors).Max(value => Math.Max(value.R, Math.Max(value.G, value.B)));
            QuietLog.Info($"battle-entrance actor={actor.FighterId} region={region} bright96={bright} vertex_max={color:0.000000} visible={actor.Root.Visible}");
            ProbeRequirement.Require(bright == 0, $"entrance retained bright fighter={actor.FighterId} pixels={bright}");
            ProbeRequirement.Require(actor.Root.Visible && color == 0f, $"entrance actor reset invalid fighter={actor.FighterId}");
        }

        ProbeRequirement.Require(BattlePixelMetrics.MeanLuminance(image) < 3, "entrance stage/background is not black");
        ProbeRequirement.Require(!_host.Renderer.EffectiveHudVisibleForProbe && !_host.Renderer.PartnerForProbe.Visible, "entrance retained overlays");
    }

    /// <summary>Measures HUD and partner contributions separately, keeping time and phase unchanged.</summary>
    /// <param name="label">Capture filename prefix.</param>
    /// <returns>The full composed image before removing diagnostic layers.</returns>
    public async Task<Image> VerifyOverlaysAsync(string label)
    {
        var renderer = _host.Renderer;
        var full = await CaptureAsync($"{label}-composed.png");
        var arrived = renderer.LightFade >= 1f;
        ProbeRequirement.Require(_host.BuildBattleView()?.Phase == BattleFlowPhase.PlayerCommand, "entrance proof is not command-ready");
        ProbeRequirement.Require(renderer.EffectiveHudVisibleForProbe == arrived && renderer.PartnerForProbe.Visible == arrived, "overlay arrival state mismatch");
        renderer.SetHudVisibleForProbe(false);
        var noHud = await CaptureAsync($"{label}-no-hud.png");
        var hudPixels = BattlePixelMetrics.CountRgbChanges(full, noHud, new Rect2(190, 40, 450, 129));
        renderer.PartnerForProbe.Visible = false;
        var noOverlays = await CaptureAsync($"{label}-no-overlays.png");
        var partnerPixels = BattlePixelMetrics.CountRgbChanges(noHud, noOverlays, new Rect2(784, 228, 300, 180));
        QuietLog.Info($"battle-entrance overlay fade={renderer.LightFade:0.000000} hud_pixels={hudPixels} partner_pixels={partnerPixels} phase={_host.BuildBattleView()?.Phase}");
        ProbeRequirement.Require(arrived ? hudPixels > 500 && partnerPixels > 300 : hudPixels == 0 && partnerPixels == 0, "overlay rendered contribution mismatch");
        renderer.SetHudVisibleForProbe(true);
        renderer.Present(_host.BuildBattleView(), 0d);
        return full;
    }

    /// <summary>Uses independent full-minus-one-actor masks and retains the stage-only fade evidence.</summary>
    /// <param name="composed">Frame whose fade is being inspected.</param>
    /// <param name="label">Private filename prefix.</param>
    /// <returns>The sum of both actor-mask luminances.</returns>
    public async Task<double> MeasureActorsAsync(Image composed, string label)
    {
        var renderer = _host.Renderer;
        renderer.SetHudVisibleForProbe(false);
        renderer.PartnerForProbe.Visible = false;
        var full = await CaptureAsync($"{label}.png");
        double total = 0;
        foreach (var id in Actors.Select(actor => actor.FighterId))
        {
            renderer.SetModelVisibleForProbe(id, false);
            var absent = await CaptureAsync($"{label}-without-{id}.png");
            renderer.SetModelVisibleForProbe(id, true);
            var mean = renderer.LightFade == 0f ? 0 : BattlePixelMetrics.FighterAndStageLuminance(full, absent).Fighter;
            ProbeRequirement.Require(mean >= _previous.GetValueOrDefault(id), $"actor darkened fighter={id}");
            _previous[id] = mean;
            total += mean;
            QuietLog.Info($"battle-entrance sample fade={renderer.LightFade:0.00} actor={id} luminance={mean:0.0}");
        }

        foreach (var actor in Actors)
        {
            renderer.SetModelVisibleForProbe(actor.FighterId, false);
        }

        var stage = await CaptureAsync($"{label}-stage.png");
        var stageMean = BattlePixelMetrics.MeanLuminance(stage);
        ProbeRequirement.Require(stageMean >= _previousStage, "stage darkened during entrance");
        _previousStage = stageMean;
        QuietLog.Info($"battle-entrance stage fade={renderer.LightFade:0.00} mean={stageMean:0.0} composed_mean={BattlePixelMetrics.MeanLuminance(composed):0.0}");
        foreach (var actor in Actors)
        {
            renderer.SetModelVisibleForProbe(actor.FighterId, true);
        }

        renderer.SetHudVisibleForProbe(true);
        renderer.Present(_host.BuildBattleView(), 0d);
        return total;
    }

    /// <summary>Projects actual source geometry to bound each actor without a guessed screen location.</summary>
    /// <param name="actor">Built fighter model.</param>
    /// <returns>A clipped half-open viewport rectangle.</returns>
    private Rect2I ActorRegion(BattleModel actor)
    {
        var points = actor.EnumeratePolygons().SelectMany(polygon => polygon.Positions.Select(position =>
            _host.Renderer.CameraForProbe.UnprojectPosition(actor.WorldPosition(polygon.Bone, position)))).ToArray();
        var x = Math.Clamp((int)MathF.Floor(points.Min(point => point.X)), 0, 1279);
        var y = Math.Clamp((int)MathF.Floor(points.Min(point => point.Y)), 0, 719);
        var right = Math.Clamp((int)MathF.Ceiling(points.Max(point => point.X)), x + 1, 1280);
        var bottom = Math.Clamp((int)MathF.Ceiling(points.Max(point => point.Y)), y + 1, 720);
        return new Rect2I(x, y, right - x, bottom - y);
    }
}
