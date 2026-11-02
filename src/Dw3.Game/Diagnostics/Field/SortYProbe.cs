using Dw3Production.Conversion;
using Dw3Production.Logging;
using Dw3Production.Simulation;
using Godot;
using static Dw3Production.Diagnostics.Field.ProbeRequirement;
using NumericsVector2 = System.Numerics.Vector2;

namespace Dw3Production.Diagnostics.Field;

/// <summary>
/// Checks sort-Y draw order between the player and a map object in S200. The probe walks below the
/// opaque object with the lowest sort line (the bank is only reachable from below), where the player
/// must be drawn in front, then renders once with the callback order reversed: the object must show
/// more texels in the wrong order than in the right one.
/// </summary>
internal sealed class SortYProbe : FieldProbe
{
    /// <summary>Upper bound of walking ticks towards the overlap position.</summary>
    private const int MaxWalkTicks = 800;

    /// <summary>Initializes a new instance of the <see cref="SortYProbe"/> class.</summary>
    /// <param name="host">Running game.</param>
    public SortYProbe(IFieldProbeHost host)
        : base(host)
    {
    }

    /// <inheritdoc/>
    protected override string FailureCode => "SORTY_PROBE";

    /// <inheritdoc/>
    protected override async Task ExecuteAsync()
    {
        var water = Host.Water?.Zones["S200"] ?? throw new InvalidOperationException("S200 water manifest unavailable");
        var sortedObj = water
            .Where(w => w.SortY > 0 && w.Frames.Count > 0 && w.Frames[0].Abr < 0)
            .OrderBy(w => w.SortY)
            .FirstOrDefault() ?? throw new InvalidOperationException("no S200 sorted opaque object in manifest");
        var captureRoot = Host.PrepareCaptureDirectory();

        // Aim below the sort line and right of the object origin, so the walk goes down-right into
        // a position where the sprites overlap.
        WalkTo(new NumericsVector2(sortedObj.X + 20, sortedObj.SortY + 16));
        Require(Host.State.ZoneId == "S200", $"sorty probe left S200 at x={Host.State.Position.X:0.#}");
        var playerY = (int)Host.State.Position.Y;
        Require(playerY > sortedObj.SortY, $"expected playerY={playerY} > sortY={sortedObj.SortY} after DownRight walk from spawn");
        Require(PlayerVisiblyOverlaps(sortedObj), $"no visible pixel overlap between player and object={sortedObj.Index} at playerY={playerY} sortY={sortedObj.SortY}");

        var target = water.First(w => w.Index == sortedObj.Index);
        var correctOrderCount = await CountVisibleObjectTexelsAsync(target, Path.Combine(captureRoot, "sorty-player-front.png"), reversed: false);
        var wrongOrderCount = await CountVisibleObjectTexelsAsync(target, Path.Combine(captureRoot, "sorty-regression.png"), reversed: true);
        Host.Renderer.Present(Host.State, Host.CurrentZone, f3On: false);
        await Host.NextFrameDrawn();

        Require(wrongOrderCount > 20, $"regression capture shows too few object pixels — object may not be fully in view wrong_order={wrongOrderCount}");
        Require(correctOrderCount < wrongOrderCount, $"sortY fix not effective: correct={correctOrderCount} wrong_order={wrongOrderCount} playerY={playerY} sortY={sortedObj.SortY}");

        // The object-in-front case needs the player above the sort line, which the floor blocks:
        // the walkable area ends 12 units left of the object, so that side is not reachable.
        QuietLog.Info($"sorty probe PASS zone=S200 object={sortedObj.Index} sortY={sortedObj.SortY} " +
            $"playerY={playerY} correct_order_visible={correctOrderCount} " +
            $"wrong_order_visible={wrongOrderCount} regression_delta={wrongOrderCount - correctOrderCount} " +
            $"object_front_side=source_inaccessible_floor_blocked captures={captureRoot}/sorty-*.png");
    }

    /// <summary>Walks with Shift held towards <paramref name="target"/> until within two units or blocked.</summary>
    /// <param name="target">World position to reach.</param>
    private void WalkTo(NumericsVector2 target)
    {
        Host.Input.Handle(new InputEventKey { Keycode = Key.Shift, Pressed = true });
        for (var tick = 0; tick < MaxWalkTicks; tick++)
        {
            if (NumericsVector2.Distance(Host.State.Position, target) < 2f || !ProbeSteering.StepToward(Host, target))
            {
                break;
            }
        }

        Host.Input.Handle(new InputEventKey { Keycode = Key.Shift, Pressed = false });
        Host.SetTestHeldDirection(SemanticDirection.None);
    }

    /// <summary>Renders in the right or the reversed callback order and counts the object texels left visible.</summary>
    /// <param name="target">Sorted map object.</param>
    /// <param name="path">Capture path.</param>
    /// <param name="reversed">Render with the regression order that draws the object over the player.</param>
    /// <returns>The number of object texels visible on screen.</returns>
    private async Task<int> CountVisibleObjectTexelsAsync(WaterObject target, string path, bool reversed)
    {
        Host.Renderer.TestReverseCallbackOrder = reversed;
        Host.Renderer.Present(Host.State, Host.CurrentZone, f3On: false);
        await Host.NextFrameDrawn();
        var image = Host.CaptureViewport();
        ViewportCapture.Save(image, path, $"sorty capture write failed: {Path.GetFileName(path)}");
        var count = new MapObjectPixels(Host).CountOpaqueSourceMatches(image, target);
        Host.Renderer.TestReverseCallbackOrder = false;
        return count;
    }

    /// <summary>
    /// Tells whether one screen pixel has an opaque texel of both the player sprite and the object,
    /// proving the walk reached a real overlap before the order is compared.
    /// </summary>
    /// <param name="obj">Sorted map object.</param>
    /// <returns><see langword="true"/> when the sprites overlap on screen.</returns>
    private bool PlayerVisiblyOverlaps(WaterObject obj)
    {
        if (Host.PlayerData is null || Host.ActiveZone is null)
        {
            return false;
        }

        var frame = obj.Frames[0];
        var objectImage = ProbePixels.TryLoad(frame.OpaquePath);
        var sample = PlayerAnimation.Sample(Host.PlayerData, Host.State);
        var playerImage = objectImage is null ? null : ProbePixels.TryLoad(Host.ResolvePlayerFrame(sample.Frame));
        if (objectImage is null || playerImage is null)
        {
            return false;
        }

        var projection = FieldScreenProjection.BlockCorners(Host.CurrentZone, Host.State.Position);
        var objLeft = projection.X(obj.X + frame.OffsetX);
        var objTop = projection.Y(obj.Y + frame.OffsetY);
        var playerLeft = projection.X(Host.State.Position.X + sample.OffsetX);
        var playerTop = projection.Y(Host.State.Position.Y + sample.OffsetY);
        return ProbePixels.OpaqueTexels(objectImage).Any(texel =>
        {
            var playerX = ((objLeft + (texel.X * 3) + 1) - playerLeft - 1) / 3;
            var playerY = ((objTop + (texel.Y * 3) + 1) - playerTop - 1) / 3;
            return playerX >= 0 && playerY >= 0 && playerX < playerImage.GetWidth() && playerY < playerImage.GetHeight()
                && ProbePixels.IsOpaque(playerImage.GetPixel(playerX, playerY));
        });
    }
}
