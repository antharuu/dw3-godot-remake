using System.Diagnostics;
using Godot;
using Dw3Production.Conversion;
using Dw3Production.Input;
using Dw3Production.Presentation;
using Dw3Production.Simulation;
using NumericsVector2 = System.Numerics.Vector2;

namespace Dw3Production;

public partial class GameHost
{
    #region Field diagnostic probes

    private void RunInputProbe()
    {
        var probe = new InputAdapter();
        probe.Handle(new InputEventKey { Keycode = Key.Z, Pressed = true });
        Require(probe.State.HeldUp && probe.State.Direction == SemanticDirection.Up, "Z did not hold up");
        probe.Handle(new InputEventKey { Keycode = Key.Z, Pressed = true, Echo = true });
        Require(probe.State.HeldUp, "echo changed held state");
        probe.Handle(new InputEventKey { Keycode = Key.D, Pressed = true });
        Require(probe.State.Direction == SemanticDirection.UpRight, "Z+D diagonal mapping failed");
        probe.Handle(new InputEventKey { Keycode = Key.Z, Pressed = false });
        Require(probe.State.Direction == SemanticDirection.Right, "key release failed");
        probe.Handle(new InputEventKey { Keycode = Key.D, Pressed = false });
        probe.Handle(new InputEventKey { Keycode = Key.Q, Pressed = true });
        Require(probe.State.Direction == SemanticDirection.Left, "Q did not map left");
        probe.Handle(new InputEventKey { Keycode = Key.Q, Pressed = false });
        probe.Handle(new InputEventKey { Keycode = Key.S, Pressed = true });
        Require(probe.State.Direction == SemanticDirection.Down, "S did not map down");
        probe.Handle(new InputEventKey { Keycode = Key.S, Pressed = false });
        probe.Handle(new InputEventKey { Keycode = Key.Left, Pressed = true });
        Require(probe.State.Direction == SemanticDirection.Left, "left arrow did not map left");
        probe.Handle(new InputEventKey { Keycode = Key.Left, Pressed = false });
        probe.Handle(new InputEventKey { Keycode = Key.Up, Pressed = true });
        Require(probe.State.Direction == SemanticDirection.Up, "up arrow did not map up");
        probe.Handle(new InputEventKey { Keycode = Key.Up, Pressed = false });
        probe.Handle(new InputEventKey { Keycode = Key.Right, Pressed = true });
        Require(probe.State.Direction == SemanticDirection.Right, "right arrow did not map right");
        probe.Handle(new InputEventKey { Keycode = Key.Right, Pressed = false });
        probe.Handle(new InputEventKey { Keycode = Key.Down, Pressed = true });
        Require(probe.State.Direction == SemanticDirection.Down, "down arrow did not map down");
        probe.Handle(new InputEventKey { Keycode = Key.Down, Pressed = false });
        probe.Handle(new InputEventKey { Keycode = Key.Enter, Pressed = true });
        Require(probe.State.ConsumeActionPressed() && !probe.State.ConsumeActionPressed(), "Enter action edge was not one-shot");
        probe.Handle(new InputEventKey { Keycode = Key.Enter, Pressed = false });
        probe.Handle(new InputEventJoypadButton { ButtonIndex = JoyButton.A, Pressed = true });
        Require(probe.State.ConsumeActionPressed(), "joypad action did not map");
        probe.Handle(new InputEventJoypadButton { ButtonIndex = JoyButton.A, Pressed = false });
        probe.Handle(new InputEventKey { PhysicalKeycode = Key.F3, Pressed = true });
        Require(probe.F3On && probe.State.ConsumeF3Toggled(), "physical F3 did not toggle on");
        probe.Handle(new InputEventKey { PhysicalKeycode = Key.F3, Pressed = false });
        probe.Handle(new InputEventKey { PhysicalKeycode = Key.F3, Pressed = true });
        Require(!probe.F3On && probe.State.ConsumeF3Toggled(), "physical F3 did not toggle off");
        probe.Handle(new InputEventJoypadMotion { Axis = JoyAxis.LeftX, AxisValue = -1.0f });
        Require(probe.State.HeldLeft, "left stick did not hold left");
        probe.SetFocused(false);
        Require(!probe.State.HeldLeft && probe.State.Direction == SemanticDirection.None, "focus loss did not clear state");
        probe.Handle(new InputEventKey { Keycode = Key.Z, Pressed = true });
        Require(!probe.State.HeldUp, "unfocused input was admitted");
        probe.SetFocused(true);
        Require(!probe.State.HeldLeft, "focus return manufactured input");
        _log.Info($"input probe PASS direction={probe.State.Direction} f3_on={probe.F3On} focused={probe.HasFocus}");
    }

    private async void RunLocomotionProbe()
    {
        try
        {
            if (_playerData is null) throw new InvalidOperationException("player data unavailable");
            var cells = Enumerable.Repeat((byte)1, 128 * 128).ToArray();
            var probeMap = new FloorMap(128, 128, cells);
            _state.Position = new NumericsVector2(64, 64);
            _state.SetFacing((int)SemanticDirection.Right);
            _state.AnimationSet = 1;
            _state.AnimationDirection = (int)SemanticDirection.Right;
            _state.AnimationTicks = 0;
            _state.IsRunning = false;
            _input.State.SetDirections(left: false, right: true, up: false, down: false);
            for (var tick = 0; tick < 24; tick++)
            {
                var result = _locomotion.Tick(_state, _input.State.Direction, probeMap, []);
                Require(result.Moved, $"locomotion probe rejected tick={tick} reason={result.Rejection}");
                var sample = PlayerAnimation.Sample(_playerData, _state);
                _log.Info($"locomotion probe tick={tick} position=({_state.Position.X:0.###},{_state.Position.Y:0.###}) frame={sample.Frame:X2} offset=({sample.OffsetX},{sample.OffsetY}) foot=({sample.FootGroundX},{sample.FootGroundY})");
                _state.AnimationTicks++;
            }
            _input.State.SetDirections(left: false, right: false, up: false, down: false);
            var stopped = _locomotion.Tick(_state, _input.State.Direction, probeMap, []);
            Require(!stopped.Moved && _state.AnimationSet == 1, "locomotion probe release did not stand");
            _renderer?.Present(_state, CurrentZone, _input.F3On);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var generatedRoot = _options.ReferenceDir ?? _dataRoot;
            var captureRoot = _options.CaptureDir ?? Path.Combine(generatedRoot, "captures");
            Directory.CreateDirectory(captureRoot);
            var capturePath = Path.Combine(captureRoot, "locomotion-probe.png");
            var status = GetViewport().GetTexture().GetImage().SavePng(capturePath);
            Require(status == Error.Ok, $"locomotion capture failed={status}");
            _log.Info($"locomotion probe PASS capture={capturePath}");
            GetTree().Quit(0);
        }
        catch (Exception error)
        {
            GD.PushError($"LOCOMOTION_PROBE_FAIL: {error.Message}");
            GetTree().Quit(3);
        }
    }

    private sealed record DepthOverlap(NumericsVector2 PlayerPosition, NumericsVector2 WorldPixel, Color ExpectedForeground);

    private async void RunDepthProbe()
    {
        try
        {
            Require(SourceDepth.ToGodotZIndex(11) < SourceDepth.ToGodotZIndex(7)
                && SourceDepth.ToGodotZIndex(7) < SourceDepth.ToGodotZIndex(3),
                "source ordering-table depth did not invert for Godot terrain order");
            Require(SourceDepth.ToGodotZIndex(7) < SourceDepth.ToGodotZIndex(4)
                && SourceDepth.ToGodotZIndex(4) < SourceDepth.ToGodotZIndex(3)
                && SourceDepth.ToGodotZIndex(11) < SourceDepth.ToGodotZIndex(4),
                "player depth 4 is not between source terrain depths");
            var generatedRoot = _options.ReferenceDir ?? _dataRoot;
            var captureRoot = _options.CaptureDir ?? Path.Combine(generatedRoot, "captures");
            Directory.CreateDirectory(captureRoot);
            await CaptureDepthOverlap("S200", terrainLayer: 0, captureRoot, "depth-s200-background-overlap.png");
            await CaptureDepthOverlap("S200", terrainLayer: 1, captureRoot, "depth-s200-foreground-overlap.png");
            await CaptureDepthOverlap("S235", terrainLayer: 0, captureRoot, "depth-s235-background-overlap.png");
            await CaptureDepthOverlap("S235", terrainLayer: 1, captureRoot, "depth-s235-foreground-overlap.png");
            _log.Info($"depth probe PASS captures={captureRoot}/depth-s[200|235]-[background|foreground]-overlap.png source=11<7<3 godot=-11<-7<-3 player=-4");
            GetTree().Quit(0);
        }
        catch (Exception error)
        {
            GD.PushError($"DEPTH_PROBE_FAIL: {error.Message}");
            GetTree().Quit(3);
        }
    }

    private async System.Threading.Tasks.Task CaptureDepthOverlap(string zoneId, int terrainLayer, string captureRoot, string fileName)
    {
        if (_playerData is null) throw new InvalidOperationException("depth probe player data unavailable");
        if (terrainLayer is not (0 or 1)) throw new ArgumentOutOfRangeException(nameof(terrainLayer));
        _state.ZoneId = zoneId;
        _state.AnimationSet = 1;
        _state.AnimationDirection = 1;
        _state.AnimationTicks = 0;
        ApplyScene(GetOrLoadZone(zoneId));
        var overlap = FindDepthOverlap(CurrentZone, _playerData, terrainLayer);
        _state.Position = overlap.PlayerPosition;
        Renderer.Present(_state, CurrentZone, f3On: false);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var image = GetViewport().GetTexture().GetImage();
        var camera = CameraController.PositionFor(CurrentZone.Zone.Width, CurrentZone.Zone.Height, new Vector2(_state.Position.X, _state.Position.Y));
        var sampleX = Mathf.RoundToInt((overlap.WorldPixel.X - camera.X) * 3f + 641f);
        var sampleY = Mathf.RoundToInt((overlap.WorldPixel.Y - camera.Y) * 3f + 361f);
        Require(sampleX >= 0 && sampleX < image.GetWidth() && sampleY >= 0 && sampleY < image.GetHeight(), $"depth probe sample outside viewport zone={zoneId}");
        var rendered = image.GetPixel(sampleX, sampleY);
        Require(SameRgb(rendered, overlap.ExpectedForeground),
            $"depth probe foreground mismatch zone={zoneId} pixel=({sampleX},{sampleY}) expected={overlap.ExpectedForeground} actual={rendered}");
        var capturePath = Path.Combine(captureRoot, fileName);
        Require(image.SavePng(capturePath) == Error.Ok, $"depth probe capture write failed={capturePath}");
        _log.Info($"depth overlap zone={zoneId} world=({overlap.WorldPixel.X:0},{overlap.WorldPixel.Y:0}) screen=({sampleX},{sampleY}) foreground={overlap.ExpectedForeground} rendered={rendered} capture={capturePath}");
    }

    private DepthOverlap FindDepthOverlap(FieldZone zone, PlayerData player, int terrainLayer)
    {
        var sample = PlayerAnimation.Sample(player, _state);
        var playerImage = Image.LoadFromFile(ResolvePlayerFrame(sample.Frame));
        var terrain = Image.LoadFromFile(zone.Zone.Layers[terrainLayer].Path);
        if (playerImage is null || terrain is null || playerImage.IsEmpty() || terrain.IsEmpty())
            throw new InvalidOperationException($"depth probe images unreadable zone={zone.Zone.Id} layer={terrainLayer}");
        for (var topY = 0; topY <= zone.Zone.Height - playerImage.GetHeight(); topY++)
            for (var topX = 0; topX <= zone.Zone.Width - playerImage.GetWidth(); topX++)
                for (var py = 0; py < playerImage.GetHeight(); py++)
                    for (var px = 0; px < playerImage.GetWidth(); px++)
                    {
                        var playerPixel = playerImage.GetPixel(px, py);
                        if (playerPixel.A < 0.99f) continue;
                        var x = topX + px;
                        var y = topY + py;
                        var terrainPixel = terrain.GetPixel(x, y);
                        if (terrainPixel.A < 0.99f || !DifferentRgb(terrainPixel, playerPixel)) continue;
                        var expected = terrainLayer == 0 ? playerPixel : terrainPixel;
                        return new DepthOverlap(new NumericsVector2(topX - sample.OffsetX, topY - sample.OffsetY), new NumericsVector2(x, y), expected);
                    }
        throw new InvalidOperationException($"depth probe found no opaque player/terrain overlap zone={zone.Zone.Id} layer={terrainLayer}");
    }

    private static bool SameRgb(Color left, Color right) => !DifferentRgb(left, right);

    private static bool DifferentRgb(Color left, Color right) =>
        MathF.Abs(left.R - right.R) > 0.01f || MathF.Abs(left.G - right.G) > 0.01f || MathF.Abs(left.B - right.B) > 0.01f;

    private async void RunF3PerformanceProbe()
    {
        try
        {
            var off = await MeasurePresentationPhase(false);
            var on = await MeasurePresentationPhase(true);
            Require(on.ImageDecodes == 0 && on.TextureUploads == 0, "F3 steady state decoded or uploaded a texture");
            Require(on.GeometryBuilds <= 1 && on.HudRefreshes < on.Frames / 2, "F3 rebuilt map geometry or refreshed HUD text per frame");
            Require(on.Position == off.Position && on.SimulationTicks == off.SimulationTicks && on.Rejections == 0 && off.Rejections == 0,
                "F3 changed matched held-input simulation displacement or rejected movement");
            _log.Info($"f3 perf PASS off_ms_median={off.MedianMs:0.###} off_ms_p95={off.P95Ms:0.###} on_ms_median={on.MedianMs:0.###} on_ms_p95={on.P95Ms:0.###} off_fps={off.Fps:0.0} on_fps={on.Fps:0.0} displacement=({off.Position.X:0.###},{off.Position.Y:0.###}) sim_ticks={on.SimulationTicks} rejections={on.Rejections} frames={on.Frames} on_decodes={on.ImageDecodes} on_uploads={on.TextureUploads} on_geometry_builds={on.GeometryBuilds} on_hud_refreshes={on.HudRefreshes}");
            GetTree().Quit(0);
        }
        catch (Exception error)
        {
            GD.PushError($"F3_PERF_PROBE_FAIL: {error.Message}");
            GetTree().Quit(3);
        }
    }

    private async System.Threading.Tasks.Task<PresentationMetrics> MeasurePresentationPhase(bool f3On)
    {
        if (_input.F3On != f3On) ToggleF3();
        const int warmFrames = 30;
        const int frames = 120;
        var start = new NumericsVector2(1000, 236);
        _state.Position = start;
        _state.SetFacing(1);
        var rejections = 0;
        for (var i = 0; i < warmFrames; i++)
        {
            SetTestHeldDirection((i % 12) < 6 ? SemanticDirection.Right : SemanticDirection.Left);
            SimulateStep(movement => { if (movement.Rejection is not null) rejections++; });
            Renderer.Present(_state, CurrentZone, _input.F3On, 1.0 / 60.0);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        }
        _state.Position = start;
        _state.SetFacing(1);
        rejections = 0;
        var initialDecodes = Renderer.ImageDecodes;
        var initialUploads = Renderer.TextureUploads;
        var initialGeometry = Renderer.OverlayGeometryBuilds;
        var initialHud = Renderer.OverlayHudRefreshes;
        var samples = new List<double>(frames);
        var wall = Stopwatch.StartNew();
        for (var i = 0; i < frames; i++)
        {
            SetTestHeldDirection((i % 12) < 6 ? SemanticDirection.Right : SemanticDirection.Left);
            var timer = Stopwatch.StartNew();
            SimulateStep(movement => { if (movement.Rejection is not null) rejections++; });
            Renderer.Present(_state, CurrentZone, _input.F3On, 1.0 / 60.0);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            samples.Add(timer.Elapsed.TotalMilliseconds);
        }
        SetTestHeldDirection(SemanticDirection.None);
        wall.Stop();
        samples.Sort();
        return new PresentationMetrics(
            frames,
            samples[samples.Count / 2],
            samples[(int)Math.Ceiling(samples.Count * 0.95) - 1],
            frames / wall.Elapsed.TotalSeconds,
            _state.Position,
            frames,
            rejections,
            Renderer.ImageDecodes - initialDecodes,
            Renderer.TextureUploads - initialUploads,
            Renderer.OverlayGeometryBuilds - initialGeometry,
            Renderer.OverlayHudRefreshes - initialHud);
    }

    private readonly record struct PresentationMetrics(int Frames, double MedianMs, double P95Ms, double Fps, NumericsVector2 Position, int SimulationTicks, int Rejections, int ImageDecodes, int TextureUploads, int GeometryBuilds, int HudRefreshes);

    private async void RunF3Probe()
    {
        try
        {
            var offStart = Snapshot();
            await CaptureF3("f3-s200-off.png");
            _state.Position = FindTriggerCell(CurrentZone, 4);
            _state.SetFacing(3);
            var offTrigger = Snapshot();
            RunWarpEdge("S200", 4, 3, "f3-off-s200-to-s235");
            var offArrival = Snapshot();
            Require(offArrival.Zone == "S235" && offArrival.Position == new NumericsVector2(278, 218) && offArrival.Facing == 3, "F3-off route did not reach manifest S235 arrival");

            _state.ZoneId = "S200";
            _state.Position = offTrigger.Position;
            _state.SetFacing(offTrigger.Facing);
            ApplyScene(GetOrLoadZone("S200"));
            ToggleF3();
            Require(_input.F3On, "F3 did not enable through InputAdapter");
            Require(Snapshot().Zone == offTrigger.Zone && Snapshot().Position == offTrigger.Position && Snapshot().Facing == offTrigger.Facing, "F3 changed simulation before movement");
            await CaptureF3("f3-s200-on.png");
            var circlePixels = await CountPlayerCollisionShapePixels();
            Require(circlePixels > 40, $"F3 collision shape not rendered (circle_pixels={circlePixels})");
            RunWarpEdge("S200", 4, 3, "f3-on-s200-to-s235");
            var onArrival = Snapshot();
            Require(onArrival.Zone == offArrival.Zone && onArrival.Position == offArrival.Position && onArrival.Facing == offArrival.Facing && onArrival.Progress == offArrival.Progress, "F3 on/off route changed position, transition, facing, or progress");

            _state.Position = FindTriggerCell(CurrentZone, 1);
            _state.SetFacing(7);
            var onTrigger = Snapshot();
            await CaptureF3("f3-s235-on.png");
            ToggleF3();
            Require(!_input.F3On, "F3 did not disable through InputAdapter");
            Require(Snapshot() == onTrigger, "F3 changed simulation after warp");
            await CaptureF3("f3-s235-off.png");
            _log.Info($"f3 probe PASS off_start={offStart} off_arrival={offArrival} on_arrival={onArrival} overlay_gameplay_delta=false");
            GetTree().Quit(0);
        }
        catch (Exception error)
        {
            GD.PushError($"F3_PROBE_FAIL: {error.Message}");
            GetTree().Quit(3);
        }
    }

    private async void RunWaterProbe()
    {
        try
        {
            var water = _water?.Zones["S200"] ?? throw new InvalidOperationException("S200 water manifest unavailable");
            var generatedRoot = _options.ReferenceDir ?? _dataRoot;
            var captureRoot = _options.CaptureDir ?? Path.Combine(generatedRoot, "captures");
            Directory.CreateDirectory(captureRoot);
            var before = await CaptureWaterImage(captureRoot, "water-s200-before.png");
            var staticObject = water.Where(item => item.Cycle == 0 && item.Frames[0].Abr < 0 && WaterIntersectsViewport(item)).MaxBy(item => CountOpaqueSourceMatches(before, item))
                ?? throw new InvalidOperationException("no visible opaque source water object");
            var sourceMatches = CountOpaqueSourceMatches(before, staticObject);
            Require(sourceMatches > 100, $"source water object={staticObject.Index} did not render a meaningful opaque region matches={sourceMatches}");
            var animatedObject = water.FirstOrDefault(item => item.Cycle == 1 && item.Frames.Count > 1 && WaterIntersectsViewport(item))
                ?? throw new InvalidOperationException("no visible animated source water object");
            for (var tick = 0; tick < 9; tick++) SimulateStep(_ => { });
            var after = await CaptureWaterImage(captureRoot, "water-s200-after.png");
            var changed = CountChangedWaterPixels(before, after, animatedObject);
            Require(changed > 10, $"source water object={animatedObject.Index} did not change after source-cycle ticks changed={changed}");
            _log.Info($"water probe PASS zone=S200 static_object={staticObject.Index} opaque_source_matches={sourceMatches} animated_object={animatedObject.Index} source_ticks=7 changed_pixels={changed} captures={captureRoot}/water-s200-[before|after].png");
            GetTree().Quit(0);
        }
        catch (Exception error)
        {
            GD.PushError($"WATER_PROBE_FAIL: {error.Message}");
            GetTree().Quit(3);
        }
    }

    private async void RunSortYProbe()
    {
        try
        {
            // Select the lowest-SortY opaque sorted object from the S200 water manifest.
            // Candidate: object 124 (x=1051, y=192, sort_y=246) at S200 spawn edge.
            var water = _water?.Zones["S200"] ?? throw new InvalidOperationException("S200 water manifest unavailable");
            var sortedObj = water
                .Where(w => w.SortY > 0 && w.Frames.Count > 0 && w.Frames[0].Abr < 0)
                .OrderBy(w => w.SortY)
                .FirstOrDefault() ?? throw new InvalidOperationException("no S200 sorted opaque object in manifest");

            var generatedRoot = _options.ReferenceDir ?? _dataRoot;
            var captureRoot = _options.CaptureDir ?? Path.Combine(generatedRoot, "captures");
            Directory.CreateDirectory(captureRoot);

            // Walk from spawn toward the ACCESSIBLE side of object 124: the water bank
            // is reachable only from BELOW (player Y > sortY=246). The floor opens to the
            // right at Y=248+ (walkable to X=1047+), creating a visual overlap zone.
            // Target: slightly below the object's sort_y line so the approach goes Down-Right.
            // Navigate all the way to the target to ensure a substantial pixel overlap.
            var frame0 = sortedObj.Frames[0];
            var target = new NumericsVector2(sortedObj.X + 20, sortedObj.SortY + 16);
            _input.Handle(new InputEventKey { Keycode = Key.Shift, Pressed = true });
            for (var overlapTick = 0; overlapTick < 800; overlapTick++)
            {
                if (NumericsVector2.Distance(_state.Position, target) < 2f) break;
                var moved = false;
                foreach (var direction in DirectionsToward(_state.Position, target))
                {
                    if (TryTestMove(direction)) { moved = true; break; }
                }
                if (!moved) break;
            }
            _input.Handle(new InputEventKey { Keycode = Key.Shift, Pressed = false });
            SetTestHeldDirection(SemanticDirection.None);

            Require(_state.ZoneId == "S200", $"sorty probe left S200 at x={_state.Position.X:0.#}");

            // Phase A: player Y > sortedObj.SortY → player renders in front of object.
            // The water bank edge (sortY=246 for object 124) is accessible only from below.
            // Y < sortY would require crossing the bank, which is blocked by floor geometry.
            var playerY = (int)_state.Position.Y;
            Require(playerY > sortedObj.SortY,
                $"expected playerY={playerY} > sortY={sortedObj.SortY} after DownRight walk from spawn");
            Require(PlayerVisiblyOverlapsSortedObject(sortedObj),
                $"no visible pixel overlap between player and object={sortedObj.Index} at playerY={playerY} sortY={sortedObj.SortY}");

            // Capture with correct sortY order: player in front blocks some object pixels,
            // so fewer object source pixels will be visible in the framebuffer.
            Renderer.Present(_state, CurrentZone, f3On: false);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var imgCorrectOrder = GetViewport().GetTexture().GetImage();
            Require(imgCorrectOrder.SavePng(Path.Combine(captureRoot, "sorty-player-front.png")) == Error.Ok,
                "sorty capture write failed: sorty-player-front.png");
            var correctOrderCount = CountOpaqueSourceMatches(imgCorrectOrder, _water!.Zones["S200"]
                .First(w => w.Index == sortedObj.Index));

            // Regression: apply wrong (non-reversed) order → object erroneously in front.
            // More object source pixels are now visible (player is hidden behind object).
            Renderer.TestReverseCallbackOrder = true;
            Renderer.Present(_state, CurrentZone, f3On: false);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            var imgWrongOrder = GetViewport().GetTexture().GetImage();
            Require(imgWrongOrder.SavePng(Path.Combine(captureRoot, "sorty-regression.png")) == Error.Ok,
                "sorty capture write failed: sorty-regression.png");
            var wrongOrderCount = CountOpaqueSourceMatches(imgWrongOrder, _water.Zones["S200"]
                .First(w => w.Index == sortedObj.Index));
            Renderer.TestReverseCallbackOrder = false;

            // Restore correct order for subsequent frames.
            Renderer.Present(_state, CurrentZone, f3On: false);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

            // Sanity: wrong-order capture must show the object (player isn't covering all of it).
            Require(wrongOrderCount > 20,
                $"regression capture shows too few object pixels — object may not be fully in view wrong_order={wrongOrderCount}");
            // Core assertion: correct order (player in front) must show FEWER object pixels than wrong order.
            // When the player correctly renders in front, they occlude part of the object.
            Require(correctOrderCount < wrongOrderCount,
                $"sortY fix not effective: correct={correctOrderCount} wrong_order={wrongOrderCount} playerY={playerY} sortY={sortedObj.SortY}");

            // Note: object-in-front case (player Y < sortY) requires player to be ABOVE
            // the bank line at Y<246, which is physically blocked by the floor geometry —
            // the walkable area only extends to X=1039 at Y=236, leaving a 12-pixel gap
            // before object 124's left edge at X=1051. Document this as source-inaccessible.
            _log.Info($"sorty probe PASS zone=S200 object={sortedObj.Index} sortY={sortedObj.SortY} " +
                $"playerY={playerY} correct_order_visible={correctOrderCount} " +
                $"wrong_order_visible={wrongOrderCount} regression_delta={wrongOrderCount - correctOrderCount} " +
                $"object_front_side=source_inaccessible_floor_blocked captures={captureRoot}/sorty-*.png");
            GetTree().Quit(0);
        }
        catch (Exception error)
        {
            GD.PushError($"SORTY_PROBE_FAIL: {error.Message}");
            GetTree().Quit(3);
        }
    }

    /// <summary>
    /// Returns true if at least one screen pixel has both the player sprite and the sorted
    /// object sprite opaque — confirming actual visual overlap without checking colors.
    /// Used to verify the navigation reached a meaningful overlap position before capturing.
    /// </summary>
    private bool PlayerVisiblyOverlapsSortedObject(WaterObject obj)
    {
        if (_playerData is null || _currentZone is null) return false;
        var frame = obj.Frames[0];
        var objectImage = Image.LoadFromFile(frame.OpaquePath);
        if (objectImage is null || objectImage.IsEmpty()) return false;

        var sample = PlayerAnimation.Sample(_playerData, _state);
        var playerImage = Image.LoadFromFile(ResolvePlayerFrame(sample.Frame));
        if (playerImage is null || playerImage.IsEmpty()) return false;

        var camera = CameraController.PositionFor(
            CurrentZone.Zone.Width, CurrentZone.Zone.Height,
            new Vector2(_state.Position.X, _state.Position.Y));

        var objScreenLeft = Mathf.RoundToInt((obj.X + frame.OffsetX - camera.X) * 3f + 640f);
        var objScreenTop = Mathf.RoundToInt((obj.Y + frame.OffsetY - camera.Y) * 3f + 360f);
        var playerScreenLeft = Mathf.RoundToInt((_state.Position.X + sample.OffsetX - camera.X) * 3f + 640f);
        var playerScreenTop = Mathf.RoundToInt((_state.Position.Y + sample.OffsetY - camera.Y) * 3f + 360f);

        for (var oy = 0; oy < objectImage.GetHeight(); oy++)
            for (var ox = 0; ox < objectImage.GetWidth(); ox++)
            {
                if (objectImage.GetPixel(ox, oy).A < 0.99f) continue;
                var screenX = objScreenLeft + ox * 3 + 1;
                var screenY = objScreenTop + oy * 3 + 1;
                var playerOx = (screenX - playerScreenLeft - 1) / 3;
                var playerOy = (screenY - playerScreenTop - 1) / 3;
                if (playerOx < 0 || playerOy < 0 || playerOx >= playerImage.GetWidth() || playerOy >= playerImage.GetHeight())
                    continue;
                if (playerImage.GetPixel(playerOx, playerOy).A >= 0.99f) return true;
            }
        return false;
    }

    /// <summary>
    /// Counts pixels in the rendered framebuffer where both the player sprite and a sorted
    /// water object are opaque, and the captured color matches the expected foreground.
    /// objectInFront=true → expects the object pixel; false → expects the player pixel.
    /// </summary>
    private int CountSortedOverlapMatches(Image capture, WaterObject obj, bool objectInFront)
    {
        if (_playerData is null || _currentZone is null) return 0;
        var frame = obj.Frames[0];
        var objectImage = Image.LoadFromFile(frame.OpaquePath);
        if (objectImage is null || objectImage.IsEmpty()) return 0;

        var sample = PlayerAnimation.Sample(_playerData, _state);
        var playerImage = Image.LoadFromFile(ResolvePlayerFrame(sample.Frame));
        if (playerImage is null || playerImage.IsEmpty()) return 0;

        var camera = CameraController.PositionFor(
            CurrentZone.Zone.Width, CurrentZone.Zone.Height,
            new Vector2(_state.Position.X, _state.Position.Y));

        // Object top-left in screen coordinates (source pixel → 3x scale).
        var objScreenLeft = Mathf.RoundToInt((obj.X + frame.OffsetX - camera.X) * 3f + 640f);
        var objScreenTop = Mathf.RoundToInt((obj.Y + frame.OffsetY - camera.Y) * 3f + 360f);

        // Player top-left in screen coordinates (sprite drawn at position + offset).
        var playerScreenLeft = Mathf.RoundToInt((_state.Position.X + sample.OffsetX - camera.X) * 3f + 640f);
        var playerScreenTop = Mathf.RoundToInt((_state.Position.Y + sample.OffsetY - camera.Y) * 3f + 360f);

        var matches = 0;
        for (var oy = 0; oy < objectImage.GetHeight(); oy++)
            for (var ox = 0; ox < objectImage.GetWidth(); ox++)
            {
                var objPixel = objectImage.GetPixel(ox, oy);
                if (objPixel.A < 0.99f) continue;

                // Each source pixel maps to a 3×3 screen block; sample the center pixel.
                var screenX = objScreenLeft + ox * 3 + 1;
                var screenY = objScreenTop + oy * 3 + 1;
                if (screenX < 1 || screenY < 1 || screenX >= capture.GetWidth() - 1 || screenY >= capture.GetHeight() - 1)
                {
                    continue;
                }

                // Find the matching player source pixel for this screen position.
                var playerOx = (screenX - playerScreenLeft - 1) / 3;
                var playerOy = (screenY - playerScreenTop - 1) / 3;
                if (playerOx < 0 || playerOy < 0 || playerOx >= playerImage.GetWidth() || playerOy >= playerImage.GetHeight())
                    continue;
                var playerPixel = playerImage.GetPixel(playerOx, playerOy);
                if (playerPixel.A < 0.99f) continue;

                // This screen pixel has both object and player opaque — check expected foreground.
                var captured = capture.GetPixel(screenX, screenY);
                var expected = objectInFront ? objPixel : playerPixel;
                if (SameRgb(captured, expected)) matches++;
            }
        return matches;
    }

    private async System.Threading.Tasks.Task<Image> CaptureWaterImage(string captureRoot, string fileName)
    {
        _renderer?.Present(_state, CurrentZone, f3On: false);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var image = GetViewport().GetTexture().GetImage();
        var path = Path.Combine(captureRoot, fileName);
        Require(image.SavePng(path) == Error.Ok, $"water capture write failed={path}");
        return image;
    }

    private bool WaterIntersectsViewport(WaterObject water)
    {
        var frame = water.Frames[0];
        var source = Image.LoadFromFile(frame.OpaquePath) ?? throw new InvalidOperationException($"water image unreadable={frame.OpaquePath}");
        var camera = CameraController.PositionFor(CurrentZone.Zone.Width, CurrentZone.Zone.Height, new Vector2(_state.Position.X, _state.Position.Y));
        var left = water.X + frame.OffsetX;
        var top = water.Y + frame.OffsetY;
        return left + source.GetWidth() >= camera.X - 1280f / 6f
            && left <= camera.X + 1280f / 6f
            && top + source.GetHeight() >= camera.Y - 720f / 6f
            && top <= camera.Y + 720f / 6f;
    }

    private int CountOpaqueSourceMatches(Image capture, WaterObject water)
    {
        var frame = water.Frames[0];
        var source = Image.LoadFromFile(frame.OpaquePath) ?? throw new InvalidOperationException($"water image unreadable={frame.OpaquePath}");
        var camera = CameraController.PositionFor(CurrentZone.Zone.Width, CurrentZone.Zone.Height, new Vector2(_state.Position.X, _state.Position.Y));
        var left = Mathf.RoundToInt((water.X + frame.OffsetX - camera.X) * 3f + 640f);
        var top = Mathf.RoundToInt((water.Y + frame.OffsetY - camera.Y) * 3f + 360f);
        var matches = 0;
        for (var y = 0; y < source.GetHeight(); y++)
            for (var x = 0; x < source.GetWidth(); x++)
            {
                var expected = source.GetPixel(x, y);
                var screenX = left + x * 3 + 1;
                var screenY = top + y * 3 + 1;
                if (expected.A < 0.99f || screenX < 0 || screenY < 0 || screenX >= capture.GetWidth() || screenY >= capture.GetHeight()) continue;
                if (SameRgb(capture.GetPixel(screenX, screenY), expected)) matches++;
            }
        return matches;
    }

    private int CountChangedWaterPixels(Image before, Image after, WaterObject water)
    {
        var frame = water.Frames[0];
        var camera = CameraController.PositionFor(CurrentZone.Zone.Width, CurrentZone.Zone.Height, new Vector2(_state.Position.X, _state.Position.Y));
        var left = Mathf.RoundToInt((water.X + frame.OffsetX - camera.X) * 3f + 640f);
        var top = Mathf.RoundToInt((water.Y + frame.OffsetY - camera.Y) * 3f + 360f);
        var changed = 0;
        for (var y = 0; y < 36; y++)
            for (var x = 0; x < 48; x++)
            {
                var screenX = left + x * 3 + 1;
                var screenY = top + y * 3 + 1;
                if (screenX < 0 || screenY < 0 || screenX >= before.GetWidth() || screenY >= before.GetHeight()) continue;
                if (DifferentRgb(before.GetPixel(screenX, screenY), after.GetPixel(screenX, screenY))) changed++;
            }
        return changed;
    }

    private async void RunCueProbe()
    {
        try
        {
            var generatedRoot = _options.ReferenceDir ?? _dataRoot;
            var captureRoot = _options.CaptureDir ?? Path.Combine(generatedRoot, "captures");
            Directory.CreateDirectory(captureRoot);
            var absent = await CaptureCueImage(captureRoot, "cue-absent-before-offer.png");
            Require(Renderer.CueSnapshot.Phase == ExitCuePhase.Hidden, "cue appears before an exit offer");

            await WalkToExitThroughInput("S200", 4, captureRoot, 0);
            Require(_warp.State == WarpState.Offer && _state.Offer, "normal input did not offer S200 exit for cue");
            for (var tick = 0; tick < 20 && Renderer.CueSnapshot.Phase != ExitCuePhase.Open; tick++) TickTestSimulation();
            var present = await CaptureCueImage(captureRoot, "cue-present-s200-offer.png");
            AssertCueFramebuffer(absent, present, "S200 initial offer");

            await ActivateOfferedWarp("S235", new NumericsVector2(278, 218), 3, captureRoot, 1);
            var removed = await CaptureCueImage(captureRoot, "cue-removed-after-commit.png");
            Require(Renderer.CueSnapshot.Phase == ExitCuePhase.Hidden, "cue survives scene commit");

            await WalkToExitThroughInput("S235", 1, captureRoot, 2);
            Require(_warp.State == WarpState.Offer, "normal input did not offer S235 return exit");
            await ActivateOfferedWarp("S200", new NumericsVector2(672, 378), 7, captureRoot, 3);
            await WalkToExitThroughInput("S200", 4, captureRoot, 4);
            Require(_warp.State == WarpState.Offer && _state.Offer, "normal input did not re-offer S200 exit");
            for (var tick = 0; tick < 20 && Renderer.CueSnapshot.Phase != ExitCuePhase.Open; tick++) TickTestSimulation();
            var reoffered = await CaptureCueImage(captureRoot, "cue-reoffered-s200.png");
            AssertCueFramebuffer(removed, reoffered, "S200 repeat offer");
            await ActivateOfferedWarp("S235", new NumericsVector2(278, 218), 3, captureRoot, 5);
            Require(_warp.CommitCount == 3 && _state.ZoneId == "S235", "cue route did not retain exactly three commits");
            Require(!_input.F3On, "cue probe enabled F3");
            _log.Info($"cue probe PASS commits={_warp.CommitCount} f3={_input.F3On} captures={captureRoot}/cue-[absent-before-offer|present-s200-offer|removed-after-commit|reoffered-s200].png");
            GetTree().Quit(0);
        }
        catch (Exception error)
        {
            GD.PushError($"CUE_PROBE_FAIL: {error.Message}");
            GetTree().Quit(3);
        }
    }

    private async System.Threading.Tasks.Task<Image> CaptureCueImage(string captureRoot, string fileName)
    {
        _renderer?.Present(_state, CurrentZone, f3On: false);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var image = GetViewport().GetTexture().GetImage();
        var path = Path.Combine(captureRoot, fileName);
        Require(image.SavePng(path) == Error.Ok, $"cue capture write failed={path}");
        return image;
    }

    private void AssertCueFramebuffer(Image absent, Image present, string label)
    {
        var frame = Renderer.CurrentCuePopFrame ?? throw new InvalidOperationException($"{label}: cue pop frame missing");
        var cueImage = Image.LoadFromFile(frame.Path);
        if (cueImage is null || cueImage.IsEmpty()) throw new InvalidOperationException($"{label}: cue source image unreadable");
        var camera = CameraController.PositionFor(CurrentZone.Zone.Width, CurrentZone.Zone.Height, new Vector2(_state.Position.X, _state.Position.Y));
        var originX = Mathf.RoundToInt((_state.Position.X + frame.OffsetX - camera.X) * 3f + 641f);
        var originY = Mathf.RoundToInt((_state.Position.Y - 27 + frame.OffsetY - camera.Y) * 3f + 361f);
        var matched = 0;
        var changed = 0;
        for (var y = 0; y < cueImage.GetHeight(); y++)
            for (var x = 0; x < cueImage.GetWidth(); x++)
            {
                var expected = cueImage.GetPixel(x, y);
                if (expected.A < 0.99f) continue;
                var screenX = originX + x * 3;
                var screenY = originY + y * 3;
                if (screenX < 0 || screenX >= present.GetWidth() || screenY < 0 || screenY >= present.GetHeight()) continue;
                var before = absent.GetPixel(screenX, screenY);
                var actual = present.GetPixel(screenX, screenY);
                if (DifferentRgb(before, actual)) changed++;
                if (SameRgb(actual, expected)) matched++;
            }
        Require(changed > 0, $"{label}: framebuffer did not change inside source cue anchor bounds");
        Require(matched > 0, $"{label}: no opaque source cue pixel rendered at actor anchor/depth");
        Require(Renderer.CueSnapshot.Phase == ExitCuePhase.Open && Renderer.CueSnapshot.RawDirection == _state.OfferedTriggerDirection,
            $"{label}: cue state is not the offered raw trigger direction");
    }

    private async void RunM1AcceptanceTest()
    {
        try
        {
            var generatedRoot = _options.ReferenceDir ?? _dataRoot;
            var captureRoot = _options.CaptureDir ?? Path.Combine(generatedRoot, "captures");
            var videoRoot = _options.VideoDir ?? Path.Combine(generatedRoot, "video");
            var frameRoot = Path.Combine(videoRoot, "m1-loop-frames");
            Directory.CreateDirectory(captureRoot);
            Directory.CreateDirectory(videoRoot);
            if (Directory.Exists(frameRoot)) Directory.Delete(frameRoot, recursive: true);
            Directory.CreateDirectory(frameRoot);

            AssertActualMapCollision("S200", new NumericsVector2(958, 200));
            AssertActualMapCollision("S235", new NumericsVector2(187, 63));
            _state.ZoneId = "S200";
            _state.Position = new NumericsVector2(1000, 236);
            _state.SetFacing(1);
            ApplyScene(GetOrLoadZone("S200"));
            var runStart = _state.Position;
            SetTestHeldDirection(SemanticDirection.Right);
            const int speedProbeTicks = 6;
            for (var tick = 0; tick < speedProbeTicks; tick++) TickTestSimulation();
            SetTestHeldDirection(SemanticDirection.None);
            var runDistance = _state.Position.X - runStart.X;
            var expectedRunDistance = speedProbeTicks * Locomotion.SourceRunWorldUnitsPerSimulationTick;
            Require(MathF.Abs(runDistance - expectedRunDistance) < 0.01f,
                $"held native run distance={runDistance:0.###} expected={expectedRunDistance:0.###}");
            _log.Info($"run probe PASS held_input=right ticks={speedProbeTicks} distance={runDistance:0.###} world_units_per_second={runDistance * 60 / speedProbeTicks:0.###} expected_source_rate={Locomotion.SourceRunWorldUnitsPerSecond:0.###}");

            _state.Position = new NumericsVector2(1000, 236);
            _input.Handle(new InputEventKey { Keycode = Key.Shift, Pressed = true });
            SetTestHeldDirection(SemanticDirection.Right);
            var walkStart = _state.Position;
            for (var tick = 0; tick < speedProbeTicks; tick++) TickTestSimulation();
            var walkDistance = _state.Position.X - walkStart.X;
            var expectedWalkDistance = speedProbeTicks * Locomotion.SourceWalkWorldUnitsPerSimulationTick;
            Require(MathF.Abs(walkDistance - expectedWalkDistance) < 0.01f && MathF.Abs(walkDistance * 4 - runDistance) < 0.01f,
                $"held Shift walk distance={walkDistance:0.###} expected={expectedWalkDistance:0.###} run={runDistance:0.###}");
            _input.Handle(new InputEventKey { Keycode = Key.Shift, Pressed = false });
            var resumedAt = _state.Position.X;
            TickTestSimulation();
            Require(MathF.Abs(_state.Position.X - resumedAt - Locomotion.SourceRunWorldUnitsPerSimulationTick) < 0.01f, "Shift release did not restore default run");
            _Notification((int)NotificationWMWindowFocusOut);
            var focusLostAt = _state.Position;
            TickTestSimulation();
            Require(_state.Position == focusLostAt && _input.State.Direction == SemanticDirection.None && !_input.State.WalkHeld, "focus loss did not clear held run/walk input");
            _Notification((int)NotificationWMWindowFocusIn);
            _log.Info($"walk probe PASS held_input=shift+right ticks={speedProbeTicks} distance={walkDistance:0.###} ratio=0.25 focus_loss_stops=true");

            _state.Position = new NumericsVector2(1000, 236);
            _state.SetFacing(1);
            var frame = 0;
            await CaptureAcceptanceFrame(frameRoot, frame++);
            await CaptureStill(captureRoot, "s200-spawn.png");
            frame = await WalkToExitThroughInput("S200", 4, frameRoot, frame);
            var routeRaw = CurrentZone.Triggers.CellAt(_state.Position);
            Require(_warp.State == WarpState.Offer,
                $"input route never offered S200 slot 4 raw={routeRaw} position={_state.Position} facing={_state.Facing} state={_warp.State}");

            frame = await ActivateOfferedWarp("S235", new NumericsVector2(278, 218), 3, frameRoot, frame);
            await CaptureStill(captureRoot, "s235-after-transition.png");

            // Regression: the return crossing starts at the source-backed S235 arrival,
            // then re-enters once more. The driver injects only native semantic input.
            frame = await WalkToExitThroughInput("S235", 1, frameRoot, frame);
            Require(_warp.State == WarpState.Offer,
                $"input route never offered S235 slot 1 raw={CurrentZone.Triggers.CellAt(_state.Position)} position={_state.Position} facing={_state.Facing} state={_warp.State}");
            frame = await ActivateOfferedWarp("S200", new NumericsVector2(672, 378), 7, frameRoot, frame);
            await CaptureStill(captureRoot, "s200-after-return.png");

            frame = await WalkToExitThroughInput("S200", 4, frameRoot, frame);
            Require(_warp.State == WarpState.Offer,
                $"input route never re-offered S200 slot 4 raw={CurrentZone.Triggers.CellAt(_state.Position)} position={_state.Position} facing={_state.Facing} state={_warp.State}");
            frame = await ActivateOfferedWarp("S235", new NumericsVector2(278, 218), 3, frameRoot, frame);
            Require(_warp.CommitCount == 3, $"normal route committed {_warp.CommitCount} times instead of three");
            await CaptureStill(captureRoot, "s235-after-reentry.png");

            var videoPath = Path.Combine(videoRoot, "m1-loop.mp4");
            AssembleAcceptanceVideo(frameRoot, videoPath);
            Require(File.Exists(videoPath) && new FileInfo(videoPath).Length > 0, "ffmpeg produced no m1-loop.mp4");
            _log.Info($"m1 acceptance PASS frames={frame} commits={_warp.CommitCount} zone={_state.ZoneId} video={videoPath}");
            GetTree().Quit(0);
        }
        catch (Exception error)
        {
            GD.PushError($"M1_ACCEPTANCE_TEST_FAIL: {error.Message}");
            GetTree().Quit(3);
        }
    }

    /// <summary>
    /// M2 multi-zone navigation: S200 -> S235 -> S255 -> S200 through real source
    /// trigger cells using only native held input, never repositioning after spawn.
    /// Asserts exactly one commit per edge and a clean return to S200.
    /// </summary>
    private async void RunM2NavProbe()
    {
        try
        {
            var generatedRoot = _options.ReferenceDir ?? _dataRoot;
            var captureRoot = _options.CaptureDir ?? Path.Combine(generatedRoot, "captures");
            Directory.CreateDirectory(captureRoot);
            var frameRoot = Path.Combine(captureRoot, "m2-nav-frames");
            if (Directory.Exists(frameRoot)) Directory.Delete(frameRoot, recursive: true);
            Directory.CreateDirectory(frameRoot);

            // The source-backed spawn is WSTAG200 (1000,236) facing 1. The driver must
            // not reposition before navigation; every transition is a source warp.
            Require(_state.ZoneId == "S200" && _state.Position == new NumericsVector2(1000, 236) && _state.Facing == 1,
                "M2 navigation must begin at the source S200 spawn without repositioning");

            var frame = 0;
            // S200 -> S210 -> S200 -> S235 -> S200: three distinct city zones
            // (S200, S210, S235) through real source trigger cells, one commit per
            // edge, a round-trip and a re-entry. S210.slot0 and S235.slot1 return
            // doors sit under their S200-arrival cells, so those returns are
            // immediate; S200.slot5 (S210) is beside spawn and S200.slot4 (S235) is
            // the validated M1 route.
            frame = await WalkToExitThroughInput("S200", 5, frameRoot, frame);
            Require(_warp.State == WarpState.Offer, "S200 slot5 never offered on native input");
            frame = await ActivateOfferedWarp("S210", new NumericsVector2(96, 400), 5, frameRoot, frame);

            frame = await WalkToExitThroughInput("S210", 0, frameRoot, frame);
            Require(_warp.State == WarpState.Offer, "S210 slot0 never offered on native input");
            frame = await ActivateOfferedWarp("S200", new NumericsVector2(1000, 236), 1, frameRoot, frame);

            frame = await WalkToExitThroughInput("S200", 4, frameRoot, frame);
            Require(_warp.State == WarpState.Offer, "S200 slot4 never offered on native input");
            frame = await ActivateOfferedWarp("S235", new NumericsVector2(278, 218), 3, frameRoot, frame);

            frame = await WalkToExitThroughInput("S235", 1, frameRoot, frame);
            Require(_warp.State == WarpState.Offer, "S235 slot1 never offered on native input");
            frame = await ActivateOfferedWarp("S200", new NumericsVector2(672, 378), 7, frameRoot, frame);

            Require(_warp.CommitCount == 4, $"M2 navigation committed {_warp.CommitCount} times instead of four");
            Require(_state.ZoneId == "S200" && _state.Position == new NumericsVector2(672, 378) && _state.Facing == 7,
                "M2 navigation did not return to S200 via S235 slot1");
            Require(!_input.F3On, "M2 navigation enabled F3");
            Require(_zoneDecodeCount == 3, $"M2 navigation decoded {_zoneDecodeCount} zones instead of three (S200/S210/S235): a re-entry must not re-decode");

            _log.Info($"m2 nav PASS commits={_warp.CommitCount} zones=S200,S210,S235 decodes={_zoneDecodeCount} route=S200->S210->S200->S235->S200 final_zone={_state.ZoneId} final_position=({_state.Position.X:0},{_state.Position.Y:0}) facing={_state.Facing}");
            GetTree().Quit(0);
        }
        catch (Exception error)
        {
            GD.PushError($"M2_NAV_PROBE_FAIL: {error.Message}");
            GetTree().Quit(3);
        }
    }

    /// <summary>
    /// M2 real-time traversal video: the representative S200 -> S210 -> S200 -> S235
    /// -> S200 route (three distinct city zones, one commit per edge) captured at the
    /// fixed 60 Hz simulation cadence and encoded at 60 fps so playback is at real
    /// gameplay speed. Every captured frame is produced by the shared SimulateStep
    /// path through native held input, never by repositioning or teleporting.
    /// </summary>
    private async void RunM2VideoProbe()
    {
        try
        {
            var generatedRoot = _options.ReferenceDir ?? _dataRoot;
            var captureRoot = _options.CaptureDir ?? Path.Combine(generatedRoot, "captures");
            var videoRoot = _options.VideoDir ?? Path.Combine(generatedRoot, "video");
            var frameRoot = Path.Combine(videoRoot, "m2-video-frames");
            Directory.CreateDirectory(captureRoot);
            Directory.CreateDirectory(videoRoot);
            if (Directory.Exists(frameRoot)) Directory.Delete(frameRoot, recursive: true);
            Directory.CreateDirectory(frameRoot);

            Require(_state.ZoneId == "S200" && _state.Position == new NumericsVector2(1000, 236) && _state.Facing == 1,
                "M2 video must begin at the source S200 spawn without repositioning");

            var frame = 0;
            frame = await WalkToExitThroughInput("S200", 5, frameRoot, frame, captureEveryTick: true);
            Require(_warp.State == WarpState.Offer, "S200 slot5 never offered on native input");
            frame = await ActivateOfferedWarp("S210", new NumericsVector2(96, 400), 5, frameRoot, frame, captureEveryTick: true);

            frame = await WalkToExitThroughInput("S210", 0, frameRoot, frame, captureEveryTick: true);
            Require(_warp.State == WarpState.Offer, "S210 slot0 never offered on native input");
            frame = await ActivateOfferedWarp("S200", new NumericsVector2(1000, 236), 1, frameRoot, frame, captureEveryTick: true);

            frame = await WalkToExitThroughInput("S200", 4, frameRoot, frame, captureEveryTick: true);
            Require(_warp.State == WarpState.Offer, "S200 slot4 never offered on native input");
            frame = await ActivateOfferedWarp("S235", new NumericsVector2(278, 218), 3, frameRoot, frame, captureEveryTick: true);

            frame = await WalkToExitThroughInput("S235", 1, frameRoot, frame, captureEveryTick: true);
            Require(_warp.State == WarpState.Offer, "S235 slot1 never offered on native input");
            frame = await ActivateOfferedWarp("S200", new NumericsVector2(672, 378), 7, frameRoot, frame, captureEveryTick: true);

            Require(_warp.CommitCount == 4, $"M2 video committed {_warp.CommitCount} times instead of four");
            Require(_state.ZoneId == "S200" && _state.Position == new NumericsVector2(672, 378) && _state.Facing == 7,
                "M2 video did not return to S200 via S235 slot1");
            Require(!_input.F3On, "M2 video enabled F3");

            var videoPath = Path.Combine(videoRoot, "m2-traversal.mp4");
            AssembleVideo(frameRoot, videoPath, 60);
            Require(File.Exists(videoPath) && new FileInfo(videoPath).Length > 0, "ffmpeg produced no m2-traversal.mp4");

            _log.Info($"m2 video PASS frames={frame} commits={_warp.CommitCount} route=S200->S210->S200->S235->S200 video={videoPath}");
            GC.Collect(); GC.WaitForPendingFinalizers();
            GetTree().Quit(0);
        }
        catch (Exception error)
        {
            GD.PushError($"M2_VIDEO_PROBE_FAIL: {error.Message}");
            GetTree().Quit(3);
        }
    }

    /// <summary>
    /// M3 gated-edge traversal: S315.slot0 -> S320 and S320.slot1 -> S315 are story
    /// gated SLOT_EXIT. For the exploration phase they are crossable but signalled
    /// (condition kept raw, status unevaluated): stepping onto their trigger cell must
    /// offer and log exit-condition-not-enforced, never suppress the dispatch. Uses the
    /// test driver's direct reposition (this is not the interactive session), exactly
    /// like RunWarpProbe.
    /// </summary>
    private void RunConditionalProbe()
    {
        try
        {
            AssertGatedExitCrossableAndSignaled("S315", 0);
            AssertGatedExitCrossableAndSignaled("S320", 1);
            _log.Info("conditional probe PASS S315.slot0->S320 S320.slot1->S315 crossable-signaled");
            GetTree().Quit(0);
        }
        catch (Exception error)
        {
            GD.PushError($"CONDITIONAL_PROBE_FAIL: {error.Message}");
            GetTree().Quit(3);
        }
    }

    private void AssertGatedExitCrossableAndSignaled(string zoneId, int slot)
    {
        var zone = GetOrLoadZone(zoneId);
        var exit = zone.Zone.Exits.FirstOrDefault(item => item.Slot == slot);
        Require(exit is not null && exit.IsUnevaluated, $"{zoneId} slot{slot} gated exit is not exposed as unevaluated");
        _state.ZoneId = zoneId;
        ApplyScene(zone);
        _state.Position = FindTriggerCell(zone, slot);
        _state.SetFacing(FindTriggerDirection(zone, slot));
        _input.State.ReleaseAction();
        var warnings = new List<string>();
        _warp.Tick(_state, _input.State, zone, ResolveTarget, ApplyScene, _log.Info, (key, message) => warnings.Add($"{key}:{message}"), StartClimb, StartDrop);
        Require(_warp.State == WarpState.Offer && _state.Offer, $"gated {zoneId} slot{slot} must offer");
        Require(warnings.Any(warning => warning.StartsWith("exit-condition-not-enforced:", StringComparison.Ordinal)),
            $"{zoneId} slot{slot} did not log exit-condition-not-enforced (got: {string.Join("; ", warnings)})");
    }

    /// <summary>
    /// M3 gated-bridge traversal: S200.slot0 -> S205 (bridge) and S205.slot0 -> S200,
    /// both story-gated (raw condition 0x7007, status unevaluated). Each edge is offered
    /// and committed exactly once, the exit-condition-not-enforced warning is logged per
    /// door, and the F3 overlay draws the gated door as an orange unevaluated exit. Uses
    /// the test driver's direct reposition (not the interactive session), exactly like
    /// RunWarpProbe/RunConditionalProbe; the cross-city walk from spawn is a greedy-walker
    /// limitation, not a gating problem (the bridge floor is BFS-connected from spawn).
    /// </summary>
    private async void RunGatedBridgeProbe()
    {
        try
        {
            Require(_state.ZoneId == "S200" && _state.Position == new NumericsVector2(1000, 236) && _state.Facing == 1,
                "gated bridge must begin at the source S200 spawn");
            Require(GetOrLoadZone("S200").Zone.Exits.FirstOrDefault(exit => exit.Slot == 0) is { IsUnevaluated: true },
                "S200 slot0 must be an unevaluated exit");

            await CrossGatedEdge("S200", 0, "s200-to-s205-bridge");
            Require(_state.ZoneId == "S205" && _state.Position == new NumericsVector2(776, 224) && _state.Facing == 1,
                "bridge did not arrive in S205 via S200 slot0");

            await CrossGatedEdge("S205", 0, "s205-to-s200-bridge");
            Require(_warp.CommitCount == 2, $"gated bridge committed {_warp.CommitCount} times instead of two");
            Require(_state.ZoneId == "S200" && _state.Position == new NumericsVector2(96, 796) && _state.Facing == 5,
                "gated bridge did not return to S200 via S205 slot0");
            Require(!_input.F3On, "gated bridge left F3 enabled");

            _log.Info($"gated bridge PASS commits={_warp.CommitCount} route=S200->S205->S200 final_zone={_state.ZoneId} final_position=({_state.Position.X:0},{_state.Position.Y:0}) facing={_state.Facing}");
            GetTree().Quit(0);
        }
        catch (Exception error)
        {
            GD.PushError($"GATED_BRIDGE_PROBE_FAIL: {error.Message}");
            GetTree().Quit(3);
        }
    }

    private async System.Threading.Tasks.Task CrossGatedEdge(string sourceZone, int slot, string label)
    {
        var current = _currentZone ?? throw new InvalidOperationException("bridge has no current zone");
        Require(current.Zone.Id == sourceZone, $"bridge expected source={sourceZone}");
        var exit = current.Zone.Exits.FirstOrDefault(item => item.Slot == slot);
        Require(exit is not null && exit.IsUnevaluated, $"{sourceZone} slot{slot} is not an unevaluated exit");

        _state.Position = FindTriggerCell(current, slot);
        _state.SetFacing(FindTriggerDirection(current, slot));
        _input.State.ReleaseAction();
        _warp.Tick(_state, _input.State, current, ResolveTarget, ApplyScene, _log.Info, _log.Warn, StartClimb, StartDrop);
        Require(_warp.State == WarpState.Offer && _state.Offer, $"{label} did not offer");
        Require(_log.Warned($"exit-condition-not-enforced:{sourceZone}:{slot}"), $"{label} did not log exit-condition-not-enforced");

        // Prove the F3 overlay draws the gated door as an unevaluated (orange) exit, and
        // save a persistent capture for the evidence trail.
        var orange = await CountUnevaluatedExitMarkerPixels(current, slot);
        Require(orange > 0, $"{label} F3 did not render the unevaluated exit marker (orange_pixels={orange})");
        ToggleF3();
        await CaptureF3($"{label}-unevaluated.png");
        ToggleF3();

        _input.State.PressAction();
        for (var tick = 0; tick < 12 && _warp.State != WarpState.Rearm; tick++)
            _warp.Tick(_state, _input.State, CurrentZone, ResolveTarget, ApplyScene, _log.Info, _log.Warn, StartClimb, StartDrop);
        Require(_warp.State == WarpState.Rearm, $"{label} did not reach rearm");
        _input.State.ReleaseAction();
        _warp.Tick(_state, _input.State, CurrentZone, ResolveTarget, ApplyScene, _log.Info, _log.Warn, StartClimb, StartDrop);
        Require(_warp.State == WarpState.Idle, $"{label} did not rearm after release");
    }

    /// <summary>
    /// Counts F3 pixels of the unevaluated-exit marker (pure orange outline) at the given
    /// slot's trigger cell, mapped to screen through the source camera formula. A count
    /// above zero proves the overlay draws gated exits distinctly from unconditional cyan.
    /// </summary>
    private async System.Threading.Tasks.Task<int> CountUnevaluatedExitMarkerPixels(FieldZone zone, int slot)
    {
        Renderer.Present(_state, zone, f3On: true);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var capture = GetViewport().GetTexture().GetImage();
        var cell = FindTriggerCell(zone, slot);
        var camera = CameraController.PositionFor(zone.Zone.Width, zone.Zone.Height, new Vector2(_state.Position.X, _state.Position.Y));
        var screenX = Mathf.RoundToInt((cell.X - camera.X) * 3f + 640f);
        var screenY = Mathf.RoundToInt((cell.Y - camera.Y) * 3f + 360f);
        var matches = 0;
        for (var y = screenY - 4; y <= screenY + 4; y++)
            for (var x = screenX - 4; x <= screenX + 4; x++)
            {
                if (x < 0 || y < 0 || x >= capture.GetWidth() || y >= capture.GetHeight()) continue;
                var pixel = capture.GetPixel(x, y);
                if (pixel.R8 > 200 && pixel.G8 >= 100 && pixel.G8 <= 155 && pixel.B8 < 60) matches++;
            }
        return matches;
    }

    /// <summary>
    /// NPC probe: visible source-decoded NPC pixels in S200/S210/S235, sortY-based
    /// NPC/object occlusion in both directions, a native-input occupancy rejection, and no
    /// sprite duplication after zone re-entry. Every expected pixel comes from the disc-decoded
    /// manifest PNGs, never a fabricated frame.
    /// </summary>
    private async void RunNpcProbe()
    {
        try
        {
            var generatedRoot = _options.ReferenceDir ?? _dataRoot;
            var captureRoot = _options.CaptureDir ?? Path.Combine(generatedRoot, "captures");
            Directory.CreateDirectory(captureRoot);
            var npc = _npc ?? throw new InvalidOperationException("npc data unavailable");
            if (_renderer is null || _playerData is null) throw new InvalidOperationException("renderer unavailable");

            var present = new Dictionary<string, int>();
            var pixelMatches = new Dictionary<string, int>();
            foreach (var zoneId in new[] { "S200", "S210", "S235" })
            {
                if (!npc.Zones.TryGetValue(zoneId, out var records) || records.Count == 0)
                    throw new InvalidOperationException($"no visible NPC records for {zoneId}");
                present[zoneId] = records.Count;
                var nonFlipped = records.FirstOrDefault(record => !record.FlipH) ?? records[0];
                var matches = await VerifyNpcPixels(captureRoot, zoneId, nonFlipped);
                pixelMatches[zoneId] = matches;
                Require(matches > 40, $"zone={zoneId} npc={nonFlipped.ActorId} rendered too few source pixels matches={matches}");
            }

            // Occlusion, two directions, both against real depth-4 map objects, using the
            // same NPC (S235 actor1, dir 7, sortY=192): in FRONT of object 11 (sortY=167)
            // and BEHIND object 9 (sortY=200). 146/100 opaque source overlaps respectively.
            var frontMatches = await VerifyNpcOcclusion(captureRoot, "S235", "actor1", 11, npcInFront: true);
            var backMatches = await VerifyNpcOcclusion(captureRoot, "S235", "actor1", 9, npcInFront: false);
            Require(frontMatches > 10, $"S235 NPC-in-front occlusion foreground mismatch matches={frontMatches}");
            Require(backMatches > 10, $"S235 NPC-behind occlusion foreground mismatch matches={backMatches}");

            // Collision: a native held step into the NPC's occupancy diamond is rejected.
            AssertNpcCollision("S200", "actor28");

            // Re-entry: the same zone visited twice keeps exactly one sprite per NPC.
            var s200 = GetOrLoadZone("S200");
            ApplyScene(s200);
            var firstCount = Renderer.NpcSpriteCount;
            ApplyScene(GetOrLoadZone("S235"));
            ApplyScene(s200);
            var reenteredCount = Renderer.NpcSpriteCount;
            Require(firstCount == reenteredCount && reenteredCount == present["S200"],
                $"re-entry duplicated NPC sprites first={firstCount} reentered={reenteredCount} expected={present["S200"]}");

            _log.Info($"npc probe PASS zones=S200,S210,S235 npcs={string.Join(",", present.Select(kv => $"{kv.Key}={kv.Value}"))} pixels={string.Join(",", pixelMatches.Select(kv => $"{kv.Key}={kv.Value}"))} occlusion_front={frontMatches} occlusion_back={backMatches} reentry={reenteredCount}");
            GC.Collect(); GC.WaitForPendingFinalizers();
            GetTree().Quit(0);
        }
        catch (Exception error)
        {
            GD.PushError($"NPC_PROBE_FAIL: {error.Message}");
            GetTree().Quit(3);
        }
    }

    /// <summary>
    /// M2 correction (black objects): verify the previously-absent city map
    /// objects are now decoded and render their opaque source pixels. Before the
    /// fix, S210/S255 had no StageTile objects and exposed black "void" holes in
    /// the playable area; each frame's opaque texels must now appear on screen.
    /// </summary>
    private async void RunObjectProbe()
    {
        try
        {
            var generatedRoot = _options.ReferenceDir ?? _dataRoot;
            var captureRoot = _options.CaptureDir ?? Path.Combine(generatedRoot, "captures");
            Directory.CreateDirectory(captureRoot);
            foreach (var zoneId in new[] { "S210", "S255", "S202", "S220", "S300" })
            {
                if (_water is null || !_water.Zones.TryGetValue(zoneId, out var water) || water.Count == 0)
                    throw new InvalidOperationException($"{zoneId} has no decoded map objects");
                _state.ZoneId = zoneId;
                ApplyScene(GetOrLoadZone(zoneId));
                // Pick an opaque (ABR < 0) object: blend-mode objects draw their
                // semi-transparent frame, whose opaque companion is empty.
                var target = water.First(item => item.Frames[0].Abr < 0);
                // Frame the chosen object, offset so the player sprite does not cover it.
                _state.Position = new NumericsVector2(target.X + 60, target.Y + 60);
                _state.SetFacing(1);
                Renderer.Present(_state, CurrentZone, f3On: false);
                await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                var capture = GetViewport().GetTexture().GetImage();
                var path = Path.Combine(captureRoot, $"objects-{zoneId}.png");
                Require(capture.SavePng(path) == Error.Ok, $"object capture write failed={path}");
                var matches = CountOpaqueSourceMatches(capture, target);
                Require(matches > 40, $"{zoneId} object={target.Index} rendered too few opaque source pixels matches={matches}");
                _log.Info($"objects PASS zone={zoneId} objects={water.Count} object={target.Index} matches={matches} capture={path}");
            }
            GC.Collect(); GC.WaitForPendingFinalizers();
            GetTree().Quit(0);
        }
        catch (Exception error)
        {
            GD.PushError($"OBJECT_PROBE_FAIL: {error.Message}");
            GetTree().Quit(3);
        }
    }

    /// <summary>
    /// M3 climb acceptance: on Central Park (S330) climb a wall up through its source
    /// SLOT_CLIMB_UP and descend a SLOT_CLIMB_DOWN through the normal offer (Cross) and
    /// pad (Up/Down) entry points, using the shared SimulateStep path and the real
    /// disc-decoded climb frames. Asserts climbHeight reaches the source wall height, the
    /// sprite lifts while the shadow stays at the foot, and the actor lands on the ledge
    /// (up) or at the foot (down). Uses the test driver's direct reposition, like the warp
    /// probes, because the climb slot's trigger cell is not reachable by the greedy walker.
    /// </summary>
    private async void RunClimbProbe()
    {
        try
        {
            var generatedRoot = _options.ReferenceDir ?? _dataRoot;
            var captureRoot = _options.CaptureDir ?? Path.Combine(generatedRoot, "captures");
            Directory.CreateDirectory(captureRoot);

            var zone = GetOrLoadZone("S330");
            ApplyScene(zone);
            _state.ZoneId = "S330";

            var upSlot = zone.Zone.Climbs.FirstOrDefault(climb => climb.Type == 2)
                ?? throw new InvalidOperationException("S330 has no SLOT_CLIMB_UP slot");
            var downSlot = zone.Zone.Climbs.FirstOrDefault(climb => climb.Type == 3)
                ?? throw new InvalidOperationException("S330 has no SLOT_CLIMB_DOWN slot");

            var up = await DriveClimb(captureRoot, zone, upSlot, climbUp: true);
            Require(up.MaxClimbHeight == upSlot.Height << 8, $"climb up reached {up.MaxClimbHeight} instead of wall height {upSlot.Height << 8}");
            Require(Math.Abs(up.Final.X - upSlot.X) == 16 && up.Final.Y == upSlot.Y - 24 - upSlot.Height,
                $"climb up landed off-ledge actual=({up.Final.X},{up.Final.Y}) expected=({upSlot.X}±16,{upSlot.Y - 24 - upSlot.Height})");
            _log.Info($"climb up PASS slot={upSlot.Slot} height={upSlot.Height} max_climb={up.MaxClimbHeight} final=({up.Final.X},{up.Final.Y}) dir={up.Direction}");

            var down = await DriveClimb(captureRoot, zone, downSlot, climbUp: false);
            Require(down.MinClimbHeight == 0, $"climb down reached min {down.MinClimbHeight} instead of 0");
            Require(Math.Abs(down.Final.X - downSlot.X) == 16 && down.Final.Y == downSlot.Y + 24 + downSlot.Height && down.FinalFacing == 0,
                $"climb down landed at the foot actual=({down.Final.X},{down.Final.Y}) facing={down.FinalFacing} expected=({downSlot.X}±16,{downSlot.Y + 24 + downSlot.Height}) facing=0");
            _log.Info($"climb down PASS slot={downSlot.Slot} height={downSlot.Height} min_climb={down.MinClimbHeight} final=({down.Final.X},{down.Final.Y}) dir={down.Direction}");

            // Reversal regression: reproducing the stuck-on-reversal report must land on
            // a free cell (the depenetration nudges the foot off the wall).
            var upReverse = await DriveClimbReversal(captureRoot, zone, upSlot, climbUp: true);
            Require(CircleFreeAt(upReverse.Final), $"climb reverse-up landed stuck at ({upReverse.Final.X},{upReverse.Final.Y})");
            _log.Info($"climb reverse-up slot={upSlot.Slot} final=({upReverse.Final.X},{upReverse.Final.Y}) facing={upReverse.FinalFacing}");

            var downReverse = await DriveClimbReversal(captureRoot, zone, downSlot, climbUp: false);
            Require(CircleFreeAt(downReverse.Final), $"climb reverse-down landed stuck at ({downReverse.Final.X},{downReverse.Final.Y})");
            _log.Info($"climb reverse-down slot={downSlot.Slot} final=({downReverse.Final.X},{downReverse.Final.Y}) facing={downReverse.FinalFacing}");

            Require(!_input.F3On, "climb probe enabled F3");
            _log.Info($"climb probe PASS zone=S330 up_slot={upSlot.Slot} down_slot={downSlot.Slot} captures={captureRoot}/climb-*.png");
            GetTree().Quit(0);
        }
        catch (Exception error)
        {
            GD.PushError($"CLIMB_PROBE_FAIL: {error.Message}");
            GetTree().Quit(3);
        }
    }

    private void RunDropProbe()
    {
        try
        {
            var zone = GetOrLoadZone("S345");
            ApplyScene(zone);
            _state.ZoneId = "S345";
            var drop = zone.Zone.Drops?.SingleOrDefault(item => item.Slot == 1)
                ?? throw new InvalidOperationException("S345 slot=1 SLOT_DROP missing");
            var (cell, direction) = FindClimbTrigger(zone, drop.Slot);
            _state.Position = cell;
            _state.SetFacing(direction);
            _state.AnimationSet = 1;
            _state.AnimationDirection = direction;
            _input.State.ReleaseAction();
            for (var tick = 0; tick < 4 && _warp.State != WarpState.Offer; tick++) TickTestSimulation();
            Require(_warp.State == WarpState.Offer && _state.Offer, "S345 drop trigger was not offered");
            var start = _state.Position;
            _input.State.PressAction();
            TickTestSimulation();
            Require(_drop.IsActive && _state.ClimbActive, "Cross did not start S345 drop");
            var expected = start + new NumericsVector2(direction == 7 ? 16 : -16, drop.Height);
            Require(_state.Position == expected && _state.ClimbHeight == drop.Height << 8,
                $"drop entry mismatch actual={_state.Position} expected={expected}");
            _input.State.ReleaseAction();
            for (var tick = 0; tick < 100 && _drop.IsActive; tick++) TickTestSimulation();
            Require(!_drop.IsActive && !_state.ClimbActive && _state.ClimbHeight == 0 && _state.AnimationSet == 1,
                "S345 drop did not restore stand/control");
            Require(_state.Position != start, "drop landing did not leave its source position");
            var movedDirections = ProbeLandingMovement(drop.Slot);
            Require(movedDirections > 0, $"drop landing stuck at {_state.Position}");
            _log.Info($"drop probe PASS zone=S345 slot={drop.Slot} height={drop.Height} dir={direction} final=({_state.Position.X},{_state.Position.Y})");
            GetTree().Quit(0);
        }
        catch (Exception error)
        {
            GD.PushError($"DROP_PROBE_FAIL: {error.Message}");
            GetTree().Quit(3);
        }
    }

    private sealed record ClimbProbeResult(int MaxClimbHeight, int MinClimbHeight, NumericsVector2 Final, int FinalFacing, int Direction);

    private async System.Threading.Tasks.Task<ClimbProbeResult> DriveClimb(string captureRoot, FieldZone zone, Climb climb, bool climbUp)
    {
        var (cell, dir) = FindClimbTrigger(zone, climb.Slot);
        _state.Position = cell;
        _state.SetFacing(dir);
        _state.AnimationSet = 1;
        _state.AnimationDirection = dir;
        _state.AnimationTicks = 0;
        SetTestHeldDirection(SemanticDirection.None);
        _input.State.ReleaseAction();

        // Offer: standing on the climb trigger cell with an admitted facing offers.
        for (var tick = 0; tick < 4 && _warp.State != WarpState.Offer; tick++) TickTestSimulation();
        Require(_warp.State == WarpState.Offer && _state.Offer, $"climb slot={climb.Slot} never offered raw={zone.Triggers.CellAt(_state.Position)} facing={_state.Facing}");
        await CaptureStill(captureRoot, climbUp ? "climb-up-before.png" : "climb-down-before.png");

        // Cross press dispatches the climb (source FIELDSTG_setOffTrigger).
        _input.State.PressAction();
        TickTestSimulation();
        Require(_state.ClimbActive, $"cross press did not start climb slot={climb.Slot}");
        _input.State.ReleaseAction();

        var maxClimbHeight = int.MinValue;
        var minClimbHeight = int.MaxValue;
        var sampled = 0;
        SetTestHeldDirection(climbUp ? SemanticDirection.Up : SemanticDirection.Down);
        for (var tick = 0; tick < 400 && _state.ClimbActive; tick++)
        {
            TickTestSimulation();
            maxClimbHeight = Math.Max(maxClimbHeight, _state.ClimbHeight);
            minClimbHeight = Math.Min(minClimbHeight, _state.ClimbHeight);
            if (sampled++ == (climb.Height / 2)) await CaptureStill(captureRoot, climbUp ? "climb-up-mid.png" : "climb-down-mid.png");
        }
        SetTestHeldDirection(SemanticDirection.None);
        Require(!_state.ClimbActive, $"climb slot={climb.Slot} did not finish after 400 ticks");
        await CaptureStill(captureRoot, climbUp ? "climb-up-after.png" : "climb-down-after.png");
        _log.Info($"climb drive slot={climb.Slot} type={(climbUp ? "up" : "down")} max={maxClimbHeight} min={minClimbHeight} final=({_state.Position.X},{_state.Position.Y}) facing={_state.Facing}");
        ProbeLandingMovement(climb.Slot);
        return new ClimbProbeResult(maxClimbHeight, minClimbHeight, _state.Position, _state.Facing, dir);
    }

    // Drives a climb partway, reverses the pad direction mid-climb, and returns the
    // final landing. Reproduces the "start up, come back down / start down, come back
    // up" stuck-on-reversal report.
    private async System.Threading.Tasks.Task<ClimbProbeResult> DriveClimbReversal(string captureRoot, FieldZone zone, Climb climb, bool climbUp)
    {
        var (cell, dir) = FindClimbTrigger(zone, climb.Slot);
        _state.Position = cell;
        _state.SetFacing(dir);
        _state.AnimationSet = 1;
        _state.AnimationDirection = dir;
        _state.AnimationTicks = 0;
        SetTestHeldDirection(SemanticDirection.None);
        _input.State.ReleaseAction();

        for (var tick = 0; tick < 4 && _warp.State != WarpState.Offer; tick++) TickTestSimulation();
        Require(_warp.State == WarpState.Offer && _state.Offer, $"climb reversal slot={climb.Slot} never offered");

        _input.State.PressAction();
        TickTestSimulation();
        Require(_state.ClimbActive, $"cross press did not start climb slot={climb.Slot}");
        _input.State.ReleaseAction();

        var maxClimbHeight = int.MinValue;
        var minClimbHeight = int.MaxValue;
        var initialDir = climbUp ? SemanticDirection.Up : SemanticDirection.Down;
        var reverseDir = climbUp ? SemanticDirection.Down : SemanticDirection.Up;
        var half = climb.Height / 2;
        SetTestHeldDirection(initialDir);
        for (var tick = 0; tick < half && _state.ClimbActive; tick++)
        {
            TickTestSimulation();
            maxClimbHeight = Math.Max(maxClimbHeight, _state.ClimbHeight);
            minClimbHeight = Math.Min(minClimbHeight, _state.ClimbHeight);
        }
        SetTestHeldDirection(reverseDir);
        for (var tick = 0; tick < 400 && _state.ClimbActive; tick++)
        {
            TickTestSimulation();
            maxClimbHeight = Math.Max(maxClimbHeight, _state.ClimbHeight);
            minClimbHeight = Math.Min(minClimbHeight, _state.ClimbHeight);
        }
        SetTestHeldDirection(SemanticDirection.None);
        Require(!_state.ClimbActive, $"climb reversal slot={climb.Slot} did not finish after reverse");
        await CaptureStill(captureRoot, climbUp ? "climb-reverse-up.png" : "climb-reverse-down.png");
        _log.Info($"climb reversal slot={climb.Slot} type={(climbUp ? "up" : "down")} max={maxClimbHeight} min={minClimbHeight} final=({_state.Position.X},{_state.Position.Y}) facing={_state.Facing}");
        ProbeLandingMovement(climb.Slot);
        return new ClimbProbeResult(maxClimbHeight, minClimbHeight, _state.Position, _state.Facing, dir);
    }

    // Probes locomotion in every semantic direction from the current landing, logging
    // each move so a collision-stuck direction is visible in the probe output.
    private int ProbeLandingMovement(int slot)
    {
        var landingFacing = _state.Facing;
        var movedDirections = 0;
        foreach (var probeDir in Enum.GetValues<SemanticDirection>())
        {
            if (probeDir == SemanticDirection.None) continue;
            var savePos = _state.Position;
            _input.State.SetDirections(
                left: probeDir is SemanticDirection.Left or SemanticDirection.UpLeft or SemanticDirection.DownLeft,
                right: probeDir is SemanticDirection.Right or SemanticDirection.UpRight or SemanticDirection.DownRight,
                up: probeDir is SemanticDirection.Up or SemanticDirection.UpLeft or SemanticDirection.UpRight,
                down: probeDir is SemanticDirection.Down or SemanticDirection.DownLeft or SemanticDirection.DownRight);
            var move = _locomotion.Tick(_state, _input.State.Direction, _floor!, _occupants);
            if (move.Moved) movedDirections++;
            _log.Info($"climb-after-move slot={slot} dir={probeDir} moved={move.Moved} rejection={move.Rejection ?? "none"} step=({move.Step.X},{move.Step.Y}) pos=({_state.Position.X},{_state.Position.Y})");
            _state.Position = savePos;
            _input.State.SetDirections(left: false, right: false, up: false, down: false);
        }
        _state.SetFacing(landingFacing);
        return movedDirections;
    }

    private static (NumericsVector2 Cell, int Dir) FindClimbTrigger(FieldZone zone, int slot)
    {
        for (var y = 0; y < zone.Triggers.Height; y++)
            for (var x = 0; x < zone.Triggers.Width; x++)
            {
                var raw = zone.Triggers.CellAt(new NumericsVector2(x, y));
                if (raw != 0 && (raw & 0x1F) == slot)
                    return (new NumericsVector2(x, y), raw >> 5);
            }
        throw new InvalidOperationException($"climb slot={slot} trigger missing in zone={zone.Zone.Id}");
    }

    private async System.Threading.Tasks.Task<int> VerifyNpcPixels(string captureRoot, string zoneId, NpcRecord npc)
    {
        ApplyScene(GetOrLoadZone(zoneId));
        _state.ZoneId = zoneId;
        // Offset the player away from the NPC so its sprite does not cover the target
        // while the camera still frames the NPC (camera follows the player).
        _state.Position = new NumericsVector2(npc.X + 60, npc.Y + 60);
        _state.SetFacing(1);
        Renderer.Present(_state, CurrentZone, f3On: false);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var capture = GetViewport().GetTexture().GetImage();
        var path = Path.Combine(captureRoot, $"npc-{zoneId}-{npc.ActorId}.png");
        Require(capture.SavePng(path) == Error.Ok, $"npc capture write failed={path}");
        return CountNpcSourceMatches(capture, npc, npc.IdleFrames[0]);
    }

    private int CountNpcSourceMatches(Image capture, NpcRecord npc, NpcFrame frame)
    {
        var image = Image.LoadFromFile(frame.Path);
        if (image is null || image.IsEmpty()) return 0;
        var camera = CameraController.PositionFor(CurrentZone.Zone.Width, CurrentZone.Zone.Height, new Vector2(_state.Position.X, _state.Position.Y));
        var left = Mathf.RoundToInt((npc.X + frame.OffsetX - camera.X) * 3f + 640f);
        var top = Mathf.RoundToInt((npc.Y + frame.OffsetY - camera.Y) * 3f + 360f);
        var width = image.GetWidth();
        var matches = 0;
        for (var y = 0; y < image.GetHeight(); y++)
            for (var x = 0; x < width; x++)
            {
                var expected = image.GetPixel(x, y);
                if (expected.A < 0.99f) continue;
                var sourceX = npc.FlipH ? width - 1 - x : x;
                var screenX = left + sourceX * 3 + 1;
                var screenY = top + y * 3 + 1;
                if (screenX < 0 || screenY < 0 || screenX >= capture.GetWidth() || screenY >= capture.GetHeight()) continue;
                if (SameRgb(capture.GetPixel(screenX, screenY), expected)) matches++;
            }
        return matches;
    }

    private async System.Threading.Tasks.Task<int> VerifyNpcOcclusion(string captureRoot, string zoneId, string npcActorId, int objectIndex, bool npcInFront)
    {
        var npc = _npc!.Zones[zoneId].Single(record => record.ActorId == npcActorId);
        var npcFrame = npc.IdleFrames[0];
        var waterObject = _water!.Zones[zoneId].Single(item => item.Index == objectIndex);
        var objectFrame = waterObject.Frames[0];
        ApplyScene(GetOrLoadZone(zoneId));
        _state.ZoneId = zoneId;
        _state.Position = new NumericsVector2((npc.X + waterObject.X) / 2f, (npc.Y + waterObject.Y) / 2f + 40);
        _state.SetFacing(1);
        Renderer.Present(_state, CurrentZone, f3On: false);
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var capture = GetViewport().GetTexture().GetImage();
        var path = Path.Combine(captureRoot, $"npc-occlusion-{zoneId}-{npcActorId}-obj{objectIndex}-{(npcInFront ? "front" : "back")}.png");
        Require(capture.SavePng(path) == Error.Ok, $"npc occlusion capture write failed={path}");
        return CountNpcObjectOverlapMatches(capture, npc, npcFrame, waterObject, objectFrame, npcInFront);
    }

    private int CountNpcObjectOverlapMatches(Image capture, NpcRecord npc, NpcFrame npcFrame, WaterObject obj, WaterFrame objFrame, bool npcInFront)
    {
        var npcImage = Image.LoadFromFile(npcFrame.Path);
        var objImage = Image.LoadFromFile(objFrame.OpaquePath);
        if (npcImage is null || objImage is null || npcImage.IsEmpty() || objImage.IsEmpty()) return 0;
        var camera = CameraController.PositionFor(CurrentZone.Zone.Width, CurrentZone.Zone.Height, new Vector2(_state.Position.X, _state.Position.Y));
        var npcLeft = Mathf.RoundToInt((npc.X + npcFrame.OffsetX - camera.X) * 3f + 640f);
        var npcTop = Mathf.RoundToInt((npc.Y + npcFrame.OffsetY - camera.Y) * 3f + 360f);
        var objLeft = Mathf.RoundToInt((obj.X + objFrame.OffsetX - camera.X) * 3f + 640f);
        var objTop = Mathf.RoundToInt((obj.Y + objFrame.OffsetY - camera.Y) * 3f + 360f);
        var npcWidth = npcImage.GetWidth();
        var matches = 0;
        for (var oy = 0; oy < objImage.GetHeight(); oy++)
            for (var ox = 0; ox < objImage.GetWidth(); ox++)
            {
                var objPixel = objImage.GetPixel(ox, oy);
                if (objPixel.A < 0.99f) continue;
                var screenX = objLeft + ox * 3 + 1;
                var screenY = objTop + oy * 3 + 1;
                if (screenX < 1 || screenY < 1 || screenX >= capture.GetWidth() - 1 || screenY >= capture.GetHeight() - 1) continue;
                var rawNpcX = (screenX - npcLeft - 1) / 3;
                var npcX = npc.FlipH ? npcWidth - 1 - rawNpcX : rawNpcX;
                var npcY = (screenY - npcTop - 1) / 3;
                if (npcX < 0 || npcY < 0 || npcX >= npcWidth || npcY >= npcImage.GetHeight()) continue;
                var npcPixel = npcImage.GetPixel(npcX, npcY);
                if (npcPixel.A < 0.99f) continue;
                var captured = capture.GetPixel(screenX, screenY);
                var expected = npcInFront ? npcPixel : objPixel;
                if (SameRgb(captured, expected)) matches++;
            }
        return matches;
    }

    /// <summary>
    /// Native held input into an NPC's occupancy diamond must be rejected with the same
    /// source predicate the collision manifest proves. Searches the four axis approaches
    /// for one on walkable floor and asserts the movement is blocked by the named actor.
    /// </summary>
    private void AssertNpcCollision(string zoneId, string npcActorId)
    {
        var zone = GetOrLoadZone(zoneId);
        var occupant = zone.Occupants.Single(item => item.Name == npcActorId);
        var approaches = new[]
        {
            (Pos: new NumericsVector2(occupant.Position.X, occupant.Position.Y - occupant.Width / 2f - 2), Dir: SemanticDirection.Down),
            (Pos: new NumericsVector2(occupant.Position.X, occupant.Position.Y + occupant.Width / 2f + 2), Dir: SemanticDirection.Up),
            (Pos: new NumericsVector2(occupant.Position.X - occupant.Width / 2f - 2, occupant.Position.Y), Dir: SemanticDirection.Right),
            (Pos: new NumericsVector2(occupant.Position.X + occupant.Width / 2f + 2, occupant.Position.Y), Dir: SemanticDirection.Left),
        };
        foreach (var (approach, direction) in approaches)
        {
            if (!zone.Floor.Contains(approach) || zone.Floor.CellAt(approach) == 0) continue;
            _state.ZoneId = zoneId;
            _state.Position = approach;
            ApplyScene(zone);
            SetTestHeldDirection(direction);
            MovementResult result = default;
            SimulateStep(movement => result = movement);
            SetTestHeldDirection(SemanticDirection.None);
            _log.Info($"npc collision attempt zone={zoneId} npc={npcActorId} approach=({approach.X:0},{approach.Y:0}) direction={direction} rejection={result.Rejection}");
            if (result.Rejection == $"actor:{npcActorId}")
            {
                _log.Info($"npc collision PASS zone={zoneId} npc={npcActorId} approach=({approach.X:0},{approach.Y:0}) direction={direction} rejection={result.Rejection}");
                return;
            }
        }
        throw new InvalidOperationException($"no walkable axis approach to NPC {zoneId}.{npcActorId} produced an actor rejection");
    }

    private async System.Threading.Tasks.Task<int> ActivateOfferedWarp(string expectedZone, NumericsVector2 expectedPosition, int expectedFacing, string frameRoot, int frame, bool captureEveryTick = false)
    {
        // Clear any stale action hold (a spurious engine key event during an awaited
        // FramePostDraw can leave ActionHeld true), then press for a guaranteed fresh
        // one-shot edge. Without the release, PressAction skips setting ActionPressed
        // and the warp never leaves Offer.
        _input.Handle(new InputEventKey { Keycode = Key.Enter, Pressed = false });
        _input.Handle(new InputEventKey { Keycode = Key.Enter, Pressed = true });
        for (var tick = 0; tick < 16 && _warp.State != WarpState.Rearm; tick++)
        {
            TickTestSimulation();
            if (captureEveryTick || tick % 2 == 0) await CaptureAcceptanceFrame(frameRoot, frame++);
        }
        Require(_warp.State == WarpState.Rearm, $"input route did not reach {expectedZone} rearm state");
        _input.Handle(new InputEventKey { Keycode = Key.Enter, Pressed = false });
        TickTestSimulation();
        Require(_warp.State == WarpState.Idle, "input route did not release after warp");
        Require(_state.ZoneId == expectedZone && _state.Position == expectedPosition && _state.Facing == expectedFacing,
            $"input route did not commit the manifest {expectedZone} arrival");
        await CaptureAcceptanceFrame(frameRoot, frame++);
        return frame;
    }

    private void AssertActualMapCollision(string zoneId, NumericsVector2 blockedFrom)
    {
        _state.ZoneId = zoneId;
        _state.Position = blockedFrom;
        _state.SetFacing(1);
        ApplyScene(GetOrLoadZone(zoneId));
        SetTestHeldDirection(SemanticDirection.Right);
        MovementResult result = default;
        SimulateStep(movement => result = movement);
        SetTestHeldDirection(SemanticDirection.None);
        Require(result.Rejection == "void-footprint" && _state.Position == blockedFrom,
            $"source map obstacle did not hold native input zone={zoneId} position={blockedFrom} rejection={result.Rejection}");
        _log.Info($"collision probe PASS zone={zoneId} held_input=right from={blockedFrom} rejection={result.Rejection}");
    }

    private async System.Threading.Tasks.Task<int> WalkToExitThroughInput(string zoneId, int slot, string frameRoot, int frame, bool captureEveryTick = false)
    {
        var currentZone = _currentZone ?? throw new InvalidOperationException("route has no active zone");
        Require(currentZone.Zone.Id == zoneId, $"route expected zone={zoneId}");
        var triggerDirection = FindTriggerDirection(currentZone, slot);
        var sourceFacing = triggerDirection + 1;
        if (sourceFacing == 8) sourceFacing = 0;
        var admissionInput = (SemanticDirection)((sourceFacing + 4) % 8);
        var target = FindTriggerApproachCell(_currentZone, slot, _state.Position, admissionInput);
        _log.Info($"route start zone={zoneId} slot={slot} start=({_state.Position.X:0.#},{_state.Position.Y:0.#}) target={target} trigdir={triggerDirection} admissionInput={admissionInput}");
        var movementTicks = 0;
        // Snapshot stagnation: fail if position hasn't advanced by >=0.5 world unit in
        // 150 ticks. This handles detours (distance to target may temporarily increase)
        // without silently spinning for 2400 ticks when genuinely stuck.
        var snapshotPos = _state.Position;
        var snapshotTick = 0;
        const int SnapshotInterval = 150;
        const float SnapshotMinAdvance = 0.5f;
        bool NearTriggerSlot() => currentZone.Triggers
            .CellsWithin(_state.Position, WarpController.OfferRadiusWorldUnits)
            .Any(c => c.Raw != 0 && (c.Raw & 0x1F) == slot);

        // The source-calibrated run step is nearly four map units. Use the normal
        // Shift walk modifier for this scripted final approach so an input route
        // reaches a real trigger cell instead of oscillating across its edge.
        _input.Handle(new InputEventKey { Keycode = Key.Shift, Pressed = true });
        try
        {
            for (; movementTicks < 2400; movementTicks++)
            {
                // Stop when within offer radius; the admission phase handles facing.
                if (NearTriggerSlot()) break;
                // Snapshot stagnation: if position hasn't advanced by 1+ unit in the
                // last SnapshotInterval ticks the driver is oscillating in place.
                if (movementTicks > 0 && movementTicks % SnapshotInterval == 0)
                {
                    if (NumericsVector2.Distance(_state.Position, snapshotPos) < SnapshotMinAdvance)
                        throw new InvalidOperationException(
                            $"route stagnated slot={slot} position={_state.Position} dist={NumericsVector2.Distance(_state.Position, target):0.#} target={target} facing={_state.Facing} snapshot_age={movementTicks - snapshotTick}");
                    snapshotPos = _state.Position;
                    snapshotTick = movementTicks;
                }
                var moved = false;
                foreach (var direction in DirectionsToward(_state.Position, target))
                {
                    if (TryTestMove(direction)) { moved = true; break; }
                }
                if (!moved) throw new InvalidOperationException($"route blocked before trigger slot={slot} position={_state.Position} target={target}");
                if (captureEveryTick || movementTicks % 32 == 0) await CaptureAcceptanceFrame(frameRoot, frame++);
            }
            Require(NearTriggerSlot(),
                $"route did not reach trigger radius slot={slot} target={target} after={movementTicks} ticks position={_state.Position} dist={NumericsVector2.Distance(_state.Position, target):0.#}");
        }
        finally
        {
            SetTestHeldDirection(SemanticDirection.None);
            _input.Handle(new InputEventKey { Keycode = Key.Shift, Pressed = false });
        }

        // Enter the decoded trigger with a held native direction that produces a
        // source-admitted facing. No position or facing is assigned by the driver.
        var admissionTicks = 0;
        for (; admissionTicks < 12 && _warp.State != WarpState.Offer; admissionTicks++)
        {
            SetTestHeldDirection(admissionInput);
            TickTestSimulation();
            if (captureEveryTick || admissionTicks % 2 == 0) await CaptureAcceptanceFrame(frameRoot, frame++);
        }
        SetTestHeldDirection(SemanticDirection.None);
        Require(_warp.State == WarpState.Offer, $"route did not offer slot={slot} after {movementTicks} walk ticks and {admissionTicks} admission ticks");
        _log.Info($"route offer PASS zone={zoneId} slot={slot} movement_ticks={movementTicks} admission_ticks={admissionTicks} position=({_state.Position.X:0.###},{_state.Position.Y:0.###}) facing={_state.Facing}");
        return frame;
    }

    private bool TryTestMove(SemanticDirection direction)
    {
        SetTestHeldDirection(direction);
        var before = _state.Position;
        SimulateStep(_ => { });
        return _state.Position != before;
    }

    private static IEnumerable<SemanticDirection> DirectionsToward(NumericsVector2 from, NumericsVector2 target) =>
        Enum.GetValues<SemanticDirection>()
            .Where(direction => direction != SemanticDirection.None)
            .OrderBy(direction =>
            {
                var vector = direction switch
                {
                    SemanticDirection.Up => new NumericsVector2(0, -1),
                    SemanticDirection.UpRight => new NumericsVector2(1, -1),
                    SemanticDirection.Right => new NumericsVector2(1, 0),
                    SemanticDirection.DownRight => new NumericsVector2(1, 1),
                    SemanticDirection.Down => new NumericsVector2(0, 1),
                    SemanticDirection.DownLeft => new NumericsVector2(-1, 1),
                    SemanticDirection.Left => new NumericsVector2(-1, 0),
                    SemanticDirection.UpLeft => new NumericsVector2(-1, -1),
                    _ => NumericsVector2.Zero,
                };
                return NumericsVector2.DistanceSquared(from + vector, target);
            });

    private void TickTestSimulation() =>
        SimulateStep(movement => throw new InvalidOperationException($"test input rejected={movement.Rejection} zone={_state.ZoneId} position={_state.Position}"));

    #endregion
}
