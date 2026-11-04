using Dw3Production.Diagnostics.Field;
using Dw3Production.Logging;
using Godot;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Checks that decoded face rectangles replace Kotemon eye texels in the framebuffer.</summary>
internal sealed class BattleFaceProbe : BattleProbe
{
    /// <summary>Initializes a new instance of the <see cref="BattleFaceProbe"/> class.</summary>
    /// <param name="host">Restricted live battle host.</param>
    public BattleFaceProbe(IBattleProbeHost host)
        : base(host)
    {
    }

    /// <inheritdoc/>
    protected override string FailureCode => "BATTLE_FACE_PROBE";

    /// <inheritdoc/>
    protected override async Task ExecuteAsync()
    {
        var captureRoot = Host.PrepareCaptureDirectory("probe/battle-face");
        Host.StartDebugBattle();
        ProbeRequirement.Require(Host.RenderedPlayerId == 383, "face probe requires Kotemon 383");
        var renderer = Host.Renderer;
        ProbeRequirement.Require(renderer.ModelCount == 2, "face probe requires two decoded fighters");
        renderer.SetProbePose(10);
        renderer.SetFaceRandomForProbe(383, 0);
        renderer.SetFaceBlinkTimerForProbe(383, 0);
        var open = await BattleProbeCapture.CaptureAsync(Host, captureRoot, "face-open.png");
        ProbeRequirement.Require(renderer.SavePartnerView(Path.Combine(captureRoot, "partner-face-open.png")) == Error.Ok, "could not save open partner face target");
        renderer.SetFaceBlinkTimerForProbe(383, 6);
        var textureChanged = renderer.FaceChangedTexels(383);
        var partnerTextureChanged = renderer.PartnerFaceChangedTexels();
        ProbeRequirement.Require(textureChanged > 0, "closed eyes did not change the destination FaceRect texels");
        ProbeRequirement.Require(partnerTextureChanged == textureChanged, "partner face page did not receive the active fighter face frame");
        var closed = await BattleProbeCapture.CaptureAsync(Host, captureRoot, "face-closed.png");
        ProbeRequirement.Require(renderer.SavePartnerView(Path.Combine(captureRoot, "partner-face-closed.png")) == Error.Ok, "could not save closed partner face target");
        var changed = BattlePixelMetrics.CountDifferent(open, closed, 0, open.GetWidth() / 2);
        ProbeRequirement.Require(changed > 0, "forced closed eyes did not change Kotemon framebuffer pixels");
        QuietLog.Info($"battle-face {renderer.FaceSummary(383)} blink=open-to-closed changed_pixels={changed} localized_texels={textureChanged} partner_localized_texels={partnerTextureChanged}");
        QuietLog.Info("battle-face fighter=0 parts=0 no_effect=true");
        QuietLog.Info($"battle-face probe PASS captures={captureRoot}/face-open.png,{captureRoot}/face-closed.png,{captureRoot}/partner-face-open.png,{captureRoot}/partner-face-closed.png");
    }
}
