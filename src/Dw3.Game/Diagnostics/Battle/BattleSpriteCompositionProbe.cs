using System.Globalization;
using Dw3.Data;
using Dw3Production.Conversion;
using Dw3Production.Diagnostics.Field;
using Dw3Production.Presentation;
using Godot;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>
/// Held real-render proof of the sprite composition: one models-layer effect deliberately behind the
/// nearest fighter, then in front of it, on the same camera ray and pose; a front-layer effect over the
/// HUD; and a full-screen source backdrop on the 16:9 viewport. Nothing advances between paired captures.
/// </summary>
internal sealed class BattleSpriteCompositionProbe : BattleProbe
{
    /// <summary>Anchor depth factor of the behind case: a sprite's ordering entry is its view depth / 64 and a
    /// polygon's is its average view depth / 16, so the sprite must be over four times farther to sort behind.</summary>
    private const float BehindFactor = 5f;

    /// <summary>Initializes a new instance of the <see cref="BattleSpriteCompositionProbe"/> class.</summary>
    /// <param name="host">Restricted offscreen host.</param>
    public BattleSpriteCompositionProbe(IBattleProbeHost host)
        : base(host)
    {
    }

    /// <inheritdoc/>
    protected override string FailureCode => "B9056C";

    /// <inheritdoc/>
    protected override async Task ExecuteAsync()
    {
        Host.ApplyS200();
        using var loader = new BattleDataPackLoader(new DataPackReader(System.Environment.GetEnvironmentVariable("DW3_DATA_DIR") ?? throw new InvalidOperationException("Explicit pack required.")));
        var scene = new BattleSpriteCompositionScene(Host, Host.PrepareCaptureDirectory("probe/battle-sprite-composition"));
        await PrepareAsync(loader);
        bool depth = await DepthCasesAsync(scene);
        bool hud = await HudCaseAsync(scene);
        bool backdrop = await BackdropCaseAsync(scene);
        GD.Print($"B9056C_SUMMARY depth={Verdict(depth)} front_over_hud={Verdict(hud)} backdrop_16x9={Verdict(backdrop)}");
        ProbeRequirement.Require(depth && hud && backdrop, "composition requirements failed");
        GD.Print("B9056C_PASS composition");
    }

    /// <summary>Formats one verdict.</summary>
    /// <param name="passed">Whether the case passed.</param>
    /// <returns>PASS or FAIL.</returns>
    private static string Verdict(bool passed) => passed ? "PASS" : "FAIL";

    /// <summary>Reads an integer setting.</summary>
    /// <param name="name">Environment variable.</param>
    /// <param name="fallback">Default value.</param>
    /// <returns>The value.</returns>
    private static int Setting(string name, int fallback) =>
        int.TryParse(System.Environment.GetEnvironmentVariable(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value) ? value : fallback;

    /// <summary>Converts a Godot world point back to the source battle-world script units.</summary>
    /// <param name="world">Godot world point.</param>
    /// <returns>The script anchor.</returns>
    private static (short X, short Y, short Z) ToScript(Vector3 world)
    {
        Vector3 source = new Vector3(world.X, -world.Y, -world.Z) * BattleSceneMath.Ps1UnitsPerGodotUnit;
        ProbeRequirement.Require(Mathf.Abs(source.X) < short.MaxValue && Mathf.Abs(source.Y) < short.MaxValue && Mathf.Abs(source.Z) < short.MaxValue - 1, $"anchor {source} leaves the script range");
        return ((short)Mathf.RoundToInt(source.X), (short)Mathf.RoundToInt(source.Y), (short)Mathf.RoundToInt(source.Z));
    }

    /// <summary>Measures how much of the effect's footprint over the model actually changes the composed frame.</summary>
    /// <param name="scene">Capture helpers.</param>
    /// <param name="model">Fighter id and silhouette.</param>
    /// <param name="placement">Case label and script anchor.</param>
    /// <param name="effect">Effect id and held step.</param>
    /// <returns>Fraction of the overlap where the sprite is visible over the model.</returns>
    private static async Task<double> OcclusionAsync(BattleSpriteCompositionScene scene, (int Fighter, BattleCompositionMask Silhouette) model, (string Label, (short X, short Y, short Z) Anchor) placement, (int Id, int Steps) effect)
    {
        scene.Start(effect.Id, placement.Anchor, effect.Steps);
        BattleCompositionMask drawn = await scene.SpritePairAsync($"{placement.Label}-composed");
        BattleCompositionMask footprint = await scene.FootprintAsync(model.Fighter, $"{placement.Label}-footprint");
        scene.Sprites.Clear();
        BattleCompositionMask overlap = footprint.And(model.Silhouette);
        int over = drawn.And(overlap).Count;
        int outside = drawn.Except(model.Silhouette).Count;
        GD.Print($"B9056C_CASE {placement.Label} anchor={placement.Anchor} footprint={footprint.Count} box={footprint.Box()} overlap={overlap.Count} drawn_over_model={over} drawn_outside_model={outside}");
        ProbeRequirement.Require(overlap.Count >= 200, $"{placement.Label}: effect footprint does not cross the model ({overlap.Count})");
        ProbeRequirement.Require(outside > 0, $"{placement.Label}: effect drew nothing outside the model, the control is void");
        return over / (double)overlap.Count;
    }

    /// <summary>Checks that a front-layer effect still draws over the HUD.</summary>
    /// <param name="scene">Capture helpers.</param>
    /// <returns>Whether the front effect changes HUD pixels.</returns>
    private static async Task<bool> HudCaseAsync(BattleSpriteCompositionScene scene)
    {
        BattleCompositionMask hud = await scene.HudMaskAsync("hud");
        scene.Start(1002, (160, 40, BattleSpriteAnchor.FrontScreenMarker), Setting("DW3_COMPOSITION_FRONT_STEPS", 20));
        BattleCompositionMask drawn = await scene.SpritePairAsync("front-over-hud");
        scene.Sprites.Clear();
        int over = drawn.And(hud).Count;
        GD.Print($"B9056C_HUD effect=1002 hud={hud.Count} drawn={drawn.Count} box={drawn.Box()} drawn_over_hud={over}");
        return over > 0;
    }

    /// <summary>Checks that the full-screen source backdrop 1020 covers both lateral margins of the 16:9 viewport.</summary>
    /// <param name="scene">Capture helpers.</param>
    /// <returns>Whether each margin is covered as densely as the source screen.</returns>
    private static async Task<bool> BackdropCaseAsync(BattleSpriteCompositionScene scene)
    {
        scene.Start(1020, (0, 0, BattleSpriteAnchor.BackScreenMarker), Setting("DW3_COMPOSITION_BACKDROP_STEPS", 30));
        scene.SetHudVisible(false);
        BattleCompositionMask drawn = await scene.SpritePairAsync("backdrop-16x9");
        scene.SetHudVisible(true);
        scene.Sprites.Clear();
        int margin = BattleHudLayout.OffsetX;
        int left = drawn.CountColumns(0, margin);
        int right = drawn.CountColumns(BattleHudLayout.ViewportWidth - margin, BattleHudLayout.ViewportWidth);
        int middle = drawn.CountColumns(margin, BattleHudLayout.ViewportWidth - margin);
        double density = middle / (double)(BattleHudLayout.ViewportWidth - (2 * margin));
        bool passed = middle > 0 && left >= density * margin * 0.9 && right >= density * margin * 0.9;
        GD.Print($"B9056C_BACKDROP effect=1020 box={drawn.Box()} left_margin={left} right_margin={right} source_screen={middle} verdict={Verdict(passed)}");
        return passed;
    }

    /// <summary>Starts the debug battle with the prepared catalogue, settles the command camera and holds the pose.</summary>
    /// <param name="loader">Validated pack.</param>
    /// <returns>Completion once the frame is held.</returns>
    private async Task PrepareAsync(BattleDataPackLoader loader)
    {
        Host.StartDebugBattle();
        await Host.NextFrameDrawn();
        var player = Host.Flow.State.ActivePlayerFighter ?? throw new InvalidOperationException("No player.");
        var enemy = Host.Flow.State.ActiveEnemyFighter ?? throw new InvalidOperationException("No enemy.");
        Host.Renderer.BuildModels(loader.ModelPath(player.Id), loader.ModelPath(enemy.Id), "composition player", "composition enemy", enemy.Id);
        Host.Renderer.ConfigureActionAssets(loader);
        Host.Renderer.SetOpeningCamera();
        for (int frame = 0; frame < 90; frame++)
        {
            Host.Renderer.Present(Host.BuildBattleView());
            await Host.NextFrameDrawn();
        }

        Host.Renderer.SetProbePose(0);
        Host.Renderer.SetProbeAnimationFrame(0);
    }

    /// <summary>Finds the visible fighter nearest the camera, the one that can hide an effect anchored far behind it.</summary>
    /// <param name="scene">Capture helpers.</param>
    /// <returns>Fighter id, its root view depth in Godot units and its silhouette.</returns>
    private async Task<(int Fighter, float Depth, BattleCompositionMask? Silhouette)> NearestVisibleFighterAsync(BattleSpriteCompositionScene scene)
    {
        Camera3D camera = Host.Renderer.CameraForProbe;
        var candidates = new List<(int Fighter, float Depth, BattleCompositionMask Silhouette)>();
        foreach (int id in new[] { Host.Flow.State.ActivePlayerFighter!.Id, Host.Flow.State.ActiveEnemyFighter!.Id })
        {
            float depth = -(camera.GlobalTransform.AffineInverse() * Host.Renderer.ModelForProbe(id).Root.GlobalPosition).Z;
            BattleCompositionMask silhouette = await scene.ModelMaskAsync(id, $"model-{id}");
            GD.Print($"B9056C_MODEL fighter={id} depth={depth.ToString("F3", CultureInfo.InvariantCulture)} pixels={silhouette.Count} box={silhouette.Box()}");
            candidates.Add((id, depth, silhouette));
        }

        var nearest = candidates.Where(item => item.Silhouette.Count >= 2000 && item.Depth > 0).OrderBy(item => item.Depth).ToArray();
        return nearest.Length == 0 ? (0, 0f, null) : nearest[0];
    }

    /// <summary>Plays the behind and in-front cases of one models-layer effect on the nearest fighter's camera ray.</summary>
    /// <param name="scene">Capture helpers.</param>
    /// <returns>Whether both occlusion cases hold.</returns>
    private async Task<bool> DepthCasesAsync(BattleSpriteCompositionScene scene)
    {
        Host.Renderer.SetHudVisibleForProbe(false);
        int noise = await scene.NoiseAsync("noise");
        ProbeRequirement.Require(noise == 0, $"held frame is not stable: {noise} pixels changed between two identical captures");
        (int fighter, float depth, BattleCompositionMask? found) = await NearestVisibleFighterAsync(scene);
        BattleCompositionMask model = found ?? throw new InvalidOperationException("no fighter is visible from the held camera");
        Vector2 center = model.Center() ?? throw new InvalidOperationException("nearest model drew no pixel");
        Camera3D camera = Host.Renderer.CameraForProbe;
        int id = Setting("DW3_COMPOSITION_EFFECT", 2004);
        int steps = Setting("DW3_COMPOSITION_STEPS", 12);
        GD.Print($"B9056C_NEAREST fighter={fighter} center={center}");
        double behind = await OcclusionAsync(scene, (fighter, model), ("behind", ToScript(camera.ProjectPosition(center, depth * BehindFactor))), (id, steps));
        double front = await OcclusionAsync(scene, (fighter, model), ("front", ToScript(camera.ProjectPosition(center, depth))), (id, steps));
        Host.Renderer.SetHudVisibleForProbe(true);
        bool passed = behind <= 0.05 && front >= 0.5;
        GD.Print($"B9056C_DEPTH effect={id} steps={steps} behind_drawn_over_model={behind.ToString("F3", CultureInfo.InvariantCulture)} front_drawn_over_model={front.ToString("F3", CultureInfo.InvariantCulture)} verdict={Verdict(passed)}");
        return passed;
    }
}
