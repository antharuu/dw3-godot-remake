using Dw3Production.Diagnostics.Field;
using Dw3Production.Logging;
using Dw3Production.Presentation;
using Dw3Production.Simulation;
using Godot;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Drives and verifies the sourced command HUD and partner-view presentation.</summary>
internal sealed class BattleHudProbe : BattleProbe
{
    /// <summary>Initializes a new instance of the <see cref="BattleHudProbe"/> class.</summary>
    /// <param name="host">Restricted live battle host.</param>
    public BattleHudProbe(IBattleProbeHost host)
        : base(host)
    {
    }

    /// <inheritdoc/>
    protected override string FailureCode => "BATTLE_HUD_PROBE";

    /// <summary>
    /// Runs the HUD probe: starts a debug battle, captures the command menu and partner view,
    /// and asserts sprite-slot count and cursor position against the source layout.
    /// </summary>
    /// <returns>A task that completes when the probe assertions pass or throws on failure.</returns>
    protected override async Task ExecuteAsync()
    {
        var captureRoot = Host.PrepareCaptureDirectory("probe/battle-hud");
        Host.ApplyS200();
        Host.StartDebugBattle();
        ProbeRequirement.Require(
            Host.HasActiveBattle && Host.Menu.Menu == BattleMenuKind.Root,
            "F8 did not reach the root command menu");
        Host.TickTestSimulation();
        Host.Renderer.SetCommandMenuCamera();
        var line0 = await BattleProbeCapture.CaptureAsync(Host, captureRoot, "command-menu-line0.png");
        await BattleProbeCapture.CaptureAsync(Host, captureRoot, "hud-full.png");
        ProbeRequirement.Require(Host.Menu.Cursor == 0 && Host.Menu.Lines.Count == 6, $"root menu cursor={Host.Menu.Cursor} lines={Host.Menu.Lines.Count}");
        for (var i = 0; i < 3; i++)
        {
            InjectBattleKey(Key.S);
        }

        ProbeRequirement.Require(
            Host.Menu.Menu == BattleMenuKind.Root && Host.Menu.Cursor == 3,
            $"three Down inputs did not reach root line 3 cursor={Host.Menu.Cursor} menu={Host.Menu.Menu}");
        var line3 = await BattleProbeCapture.CaptureAsync(Host, captureRoot, "command-menu-line3.png");

        // Source 320x240 coordinates are mapped exactly by BattleHudLayout: cursor at
        // (17,110 + sel*19), while WFIGHTMN frame 0x22's own part offset puts its pixels at
        // (16,106 + i*19), all scaled 3 and offset 160.
        var cursor0 = ToViewport(new Vector2(17, 110));
        var cursor3 = ToViewport(new Vector2(17, 167));
        ProbeRequirement.Require(
            CountDifferentPixelsInRect(line0, line3, new Rect2(cursor0, new Vector2(36, 36))) > 40,
            "cursor line0 pixels did not move at source=(17,110)");
        ProbeRequirement.Require(
            CountDifferentPixelsInRect(line0, line3, new Rect2(cursor3, new Vector2(36, 36))) > 40,
            "cursor line3 pixels did not move at source=(17,167)");
        var spriteSlots = 0;
        for (var i = 0; i < 6; i++)
        {
            var slot = ToViewport(new Vector2(16, 106 + (i * 19)));
            if (BattlePixelMetrics.CountBright(line3, new Rect2(slot, new Vector2(36, 36))) > 12)
            {
                spriteSlots++;
            }
        }

        ProbeRequirement.Require(spriteSlots == 6, $"source sprite 0x22 slots visible={spriteSlots}/6");
        ProbeRequirement.Require(
            BattlePixelMetrics.CountBright(line3, new Rect2(ToViewport(new Vector2(11, 15)), new Vector2(450, 129))) > 500,
            "HUD names/icons/HP gauge area has insufficient rendered pixels");
        Host.Renderer.SetHudVisibleForProbe(false);
        var noHud = await BattleProbeCapture.CaptureAsync(Host, captureRoot, "command-menu-no-hud.png");
        Host.Renderer.SetHudVisibleForProbe(true);
        var rightMargin = new Rect2(ToViewport(new Vector2(311, 70)), new Vector2((3 * 9) + 160, 3 * 70));
        var spill = CountDifferentPixelsInRect(line3, noHud, rightMargin);
        ProbeRequirement.Require(spill == 0, $"partner frame 0x0A spills right of x=310 changed={spill}");
        var partner = await BattleProbeCapture.CaptureAsync(Host, captureRoot, "partner-view.png");
        var partnerPixels = Host.Renderer.PartnerViewOpaquePixels();
        var partnerRect = new Rect2(ToViewport(new Vector2(208, 76)), new Vector2(100 * BattleHudLayout.Scale, 60 * BattleHudLayout.Scale));
        var frameRect = new Rect2(ToViewport(new Vector2(206, 74)), new Vector2(104 * BattleHudLayout.Scale, 64 * BattleHudLayout.Scale));
        var partnerTargetPath = Path.Combine(captureRoot, "partner-view-target.png");
        ProbeRequirement.Require(
            Host.Renderer.SavePartnerView(partnerTargetPath) == Error.Ok,
            "partner view target write failed");

        // The source target is layer 0x1009 at (208,76,100,60). Its raw reference uses the
        // documented night-blue panel key, while Godot clears this offscreen target to alpha 0.
        var partnerTarget = Image.LoadFromFile(partnerTargetPath);
        var partnerPsxFull = Host.LoadPsxReference("battle/kotemon-383-command-menu-0.png");
        ProbeRequirement.Require(
            partnerTarget is not null && !partnerTarget.IsEmpty(),
            $"partner view references unreadable remake={partnerTargetPath}");
        var partnerPsx = partnerPsxFull.GetRegion(new Rect2I(208, 76, 100, 60));
        var partnerRemakeBox = PartnerTargetBoundingBox(partnerTarget!);
        var partnerPsxBox = PartnerPsxBoundingBox(partnerPsx);
        ProbeRequirement.Require(
            partnerRemakeBox is not null && partnerPsxBox is not null,
            $"partner view bbox unavailable remake={partnerRemakeBox} psx={partnerPsxBox}");
        var partnerRemakeBounds = partnerRemakeBox.GetValueOrDefault();
        var partnerPsxBounds = partnerPsxBox.GetValueOrDefault();
        var partnerHeightDelta = MathF.Abs(partnerRemakeBounds.Size.Y - partnerPsxBounds.Size.Y) / partnerPsxBounds.Size.Y;
        ProbeRequirement.Require(
            partnerHeightDelta <= .25f,
            $"partner view model height delta={partnerHeightDelta:P1} exceeds 25% remake={BattleComparisonImage.FormatBounds(partnerRemakeBounds)} psx={BattleComparisonImage.FormatBounds(partnerPsxBounds)}");
        var partnerSheet = BattleComparisonImage.SideBySide(partnerTarget!, partnerPsx, "REMAKE", "PSX");
        partnerSheet.Resize(partnerSheet.GetWidth() * 4, partnerSheet.GetHeight() * 4, Image.Interpolation.Nearest);
        ProbeRequirement.Require(
            partnerSheet.SavePng(Path.Combine(captureRoot, "partner-view-side-by-side-x4.png")) == Error.Ok,
            "partner view side-by-side write failed");
        QuietLog.Info($"battle-partner-view bbox remake={BattleComparisonImage.FormatBounds(partnerRemakeBounds)} psx={BattleComparisonImage.FormatBounds(partnerPsxBounds)} height-delta={partnerHeightDelta:P1}");
        ProbeRequirement.Require(partnerPixels > 300, $"partner view target has insufficient opaque pixels={partnerPixels}");
        ProbeRequirement.Require(BattlePixelMetrics.CountBright(partner, frameRect) > 30, "partner-view frame 10 absent");
        Host.Renderer.SetPartnerModelVisibleForProbe(false);
        var withoutPartner = await BattleProbeCapture.CaptureAsync(Host, captureRoot, "command-menu-without-partner-view.png");
        Host.Renderer.SetPartnerModelVisibleForProbe(true);
        var partnerCompositePixels = CountDifferentPixelsInRect(partner, withoutPartner, partnerRect);
        ProbeRequirement.Require(
            partnerCompositePixels > 300,
            $"partner view is absent from the composed framebuffer changed={partnerCompositePixels}");

        // Ended is outside the player-turn presentation flow. Presenting it removes the layer, which proves the
        // lifecycle condition without forging a separate visibility switch in the renderer.
        Host.Renderer.Present(new BattleView(BattleFlowPhase.Ended, BattleOutcome.None, [], [], string.Empty, null));
        await Host.NextFrameDrawn();
        ProbeRequirement.Require(Host.Renderer.PartnerViewOpaquePixels() == 0, "partner view persists outside player turn");
        Host.Renderer.Present(Host.BuildBattleView());

        // The shot camera is assessed against the paired Kotemon command-menu frame.
        // Difference against the same Godot capture with Kotemon hidden gives a model mask
        // without guessing a stage colour. The PSX mask keeps warm Kotemon body texels only.
        Host.Renderer.SetModelVisibleForProbe(383, false);
        var withoutPlayer = await BattleProbeCapture.CaptureAsync(Host, captureRoot, "command-menu-without-kotemon.png");
        Host.Renderer.SetModelVisibleForProbe(383, true);
        var command = await BattleProbeCapture.CaptureAsync(Host, captureRoot, "command-menu-kotemon.png");
        var remakeBox = BattlePixelMetrics.DifferenceBounds(BattleComparisonImage.ToSourceSize(command), BattleComparisonImage.ToSourceSize(withoutPlayer));
        var psx = Host.LoadPsxReference("battle/kotemon-383-command-menu-0.png");
        var psx320 = BattleComparisonImage.ToSourceSize(psx);
        var psxBox = BattlePixelMetrics.WarmKotemonBounds(psx320);
        ProbeRequirement.Require(remakeBox is not null && psxBox is not null, $"camera bbox unavailable remake={remakeBox} psx={psxBox}");
        var remakeBounds = remakeBox.GetValueOrDefault();
        var psxBounds = psxBox.GetValueOrDefault();
        var delta = Math.Max(
            Math.Max(Math.Abs(remakeBounds.Position.X - psxBounds.Position.X), Math.Abs(remakeBounds.Position.Y - psxBounds.Position.Y)),
            Math.Max(Math.Abs(remakeBounds.Size.X - psxBounds.Size.X), Math.Abs(remakeBounds.Size.Y - psxBounds.Size.Y)));
        var compare = BattleComparisonImage.SideBySide(BattleComparisonImage.ToSourceSize(command), psx320, "REMAKE COMMAND", "PSX COMMAND");
        ProbeRequirement.Require(compare.SavePng(Path.Combine(captureRoot, "compare-command-menu-partnerview.png")) == Error.Ok, "partner-view comparison write failed");
        var suffix = delta <= 8 ? "PASS" : "CAUSE=Host.Renderer.cs:SetCommandMenuCamera uses source shot view 2 while projection/GTE rasterization remains unimplemented";
        QuietLog.Info($"battle-camera phase=command-menu psx_bbox={BattleComparisonImage.FormatBounds(psxBounds)} remake_bbox={BattleComparisonImage.FormatBounds(remakeBounds)} delta={delta:0} {suffix}");
        QuietLog.Info($"battle-hud partner-frame spill={spill} thumbnail={partnerPixels} composed={partnerCompositePixels}");
        QuietLog.Info($"battle-hud partner_view=100x60 source=(208,76) viewport=({partnerRect.Position.X:0},{partnerRect.Position.Y:0}) pixels={partnerPixels} frame10=(206,74) captures={Path.Combine(captureRoot, "partner-view.png")}");
        QuietLog.Info($"battle-hud probe PASS cursor0=(17,110)->({cursor0.X:0},{cursor0.Y:0}) cursor3=(17,167)->({cursor3.X:0},{cursor3.Y:0}) sprites={spriteSlots}/6 captures={captureRoot}");
    }

    /// <summary>
    /// Counts pixels whose RGB channels differ between two same-sized images within a rectangular region.
    /// Used to verify that a UI element (cursor, sprite) changes between two captured frames.
    /// </summary>
    /// <param name="a">First image; must have the same dimensions as <paramref name="b"/>.</param>
    /// <param name="b">Second image; must have the same dimensions as <paramref name="a"/>.</param>
    /// <param name="rect">Screen-space rectangle to examine; clamped to the image bounds.</param>
    /// <returns>Number of pixels that differ in at least one RGB channel within the rectangle.</returns>
    private static int CountDifferentPixelsInRect(Image a, Image b, Rect2 rect)
    {
        var count = 0;
        var fromX = Math.Max(0, (int)rect.Position.X);
        var toX = Math.Min(a.GetWidth(), (int)rect.End.X);
        var fromY = Math.Max(0, (int)rect.Position.Y);
        var toY = Math.Min(a.GetHeight(), (int)rect.End.Y);
        for (var y = fromY; y < toY; y++)
        {
            for (var x = fromX; x < toX; x++)
            {
                if (ProbePixels.DifferentRgb(a.GetPixel(x, y), b.GetPixel(x, y)))
                {
                    count++;
                }
            }
        }

        return count;
    }

    /// <summary>Bounds the opaque texels of the offscreen partner-view target.</summary>
    /// <param name="image">Partner-view target cleared to alpha 0.</param>
    /// <returns>The opaque bounds, or null when the target is empty.</returns>
    private static Rect2? PartnerTargetBoundingBox(Image image) =>
        Bounds(image, (x, y) => image.GetPixel(x, y).A8 != 0);

    /// <summary>Bounds the PSX partner-view texels that differ from the night-blue panel key.</summary>
    /// <param name="image">PSX partner-view region.</param>
    /// <returns>The model bounds, or null when only the panel is visible.</returns>
    private static Rect2? PartnerPsxBoundingBox(Image image)
    {
        var panel = new Color(8 / 255f, 8 / 255f, 65 / 255f);
        return Bounds(image, (x, y) => ProbePixels.DifferentRgb(image.GetPixel(x, y), panel));
    }

    /// <summary>Bounds every pixel accepted by a selector.</summary>
    /// <param name="image">Scanned image.</param>
    /// <param name="selected">Pixel selector.</param>
    /// <returns>The selected bounds, or null when no pixel is selected.</returns>
    private static Rect2? Bounds(Image image, Func<int, int, bool> selected)
    {
        var minX = image.GetWidth();
        var minY = image.GetHeight();
        var maxX = -1;
        var maxY = -1;
        for (var y = 0; y < image.GetHeight(); y++)
        {
            for (var x = 0; x < image.GetWidth(); x++)
            {
                if (!selected(x, y))
                {
                    continue;
                }

                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }
        }

        return maxX < minX ? null : new Rect2(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    /// <summary>Injects a complete native press edge through battle simulation.</summary>
    /// <param name="key">Godot key to inject.</param>
    private void InjectBattleKey(Key key)
    {
        Host.Input.Handle(new InputEventKey { Keycode = key, Pressed = true });
        Host.TickTestSimulation();
        Host.Input.Handle(new InputEventKey { Keycode = key, Pressed = false });
        Host.TickTestSimulation();
    }

    /// <summary>Maps source HUD coordinates into the 16:9 viewport.</summary>
    /// <param name="source">Source-space coordinates.</param>
    /// <returns>Viewport coordinates.</returns>
    private static Vector2 ToViewport(Vector2 source)
    {
        var (x, y) = BattleHudLayout.ToViewport(Mathf.RoundToInt(source.X), Mathf.RoundToInt(source.Y));
        return new Vector2(x, y);
    }
}
