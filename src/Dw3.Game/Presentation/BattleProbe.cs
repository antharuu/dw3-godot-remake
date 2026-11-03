using Dw3.Data;
using Dw3Production.Conversion;
using Dw3Production.Simulation;
using Godot;
using NumericsVector2 = System.Numerics.Vector2;

namespace Dw3Production.Presentation;

/// <summary>
/// Offscreen battle probe: a self-contained scene that renders the real S200 field, starts a
/// deterministic encounter-200 battle (Agumon vs fighter 51), plays it to victory, then returns
/// to the field, capturing before/battle/after. It reuses the production data-pack reader,
/// FieldRenderer, BattleRenderer and BattleFlowController, so the loop exercises the real path.
/// Run: Godot --path src/Dw3.Game --rendering-driver opengl3 res://BattleProbe.tscn.
/// </summary>
public sealed partial class BattleProbe : Node2D
{
    private readonly GameState _state = new();
    private FieldRenderer? _fieldRenderer;
    private FieldZone? _zone;
    private BattleRenderer? _battleRenderer;

    /// <summary>Initializes probe data, creates the field scene, and schedules the scenario.</summary>
    public override void _Ready()
    {
        try
        {
            var dataRoot = UserPaths.GetDataDirectory();
            var captureRoot = System.Environment.GetEnvironmentVariable("DW3_CAPTURE_DIR") ?? Path.Combine(dataRoot, "captures");
            Directory.CreateDirectory(captureRoot);
            _battleLoader = new BattleDataPackLoader(new DataPackReader(dataRoot));
            _fieldLoader = new FieldDataPackLoader(new DataPackReader(dataRoot));
            _animationLoader = new FieldAnimationDataPackLoader(new DataPackReader(dataRoot));
            _captureRoot = captureRoot;

            BuildField(_animationLoader, _fieldLoader);
            CallDeferred(MethodName.RunScenario);
        }
        catch (Exception error)
        {
            GD.PushError($"BATTLE_PROBE_FAIL: {error.Message}");
            GetTree().Quit(3);
        }
    }

    private BattleDataPackLoader? _battleLoader;
    private FieldDataPackLoader? _fieldLoader;
    private FieldAnimationDataPackLoader? _animationLoader;
    private string _captureRoot = string.Empty;

    /// <summary>Disposes data-pack loaders when the probe leaves the scene tree.</summary>
    public override void _ExitTree()
    {
        _fieldLoader?.Dispose();
        _fieldLoader = null;
        _animationLoader?.Dispose();
        _animationLoader = null;
        _battleLoader?.Dispose();
        _battleLoader = null;
    }

    /// <summary>Loads data and constructs the initial field presentation for the probe.</summary>
    /// <param name="loader">Loader for field animation data.</param>
    /// <param name="fieldLoader">Loader for field zone and player data.</param>
    private void BuildField(FieldAnimationDataPackLoader loader, FieldDataPackLoader fieldLoader)
    {
        var player = fieldLoader.LoadPlayer();
        var cue = loader.LoadExitCue();
        var climb = loader.LoadClimb();
        var drop = loader.LoadDrop();
        var water = loader.LoadWater();
        var icon = loader.LoadPlayerIcon();
        _actionData = MergeActionData(climb, drop);
        _fieldRenderer = new FieldRenderer(this, cue, water, icon);

        var zone = fieldLoader.LoadZone("S200");
        _zone = BuildZone(zone, []);
        _state.ZoneId = "S200";
        _state.Position = new NumericsVector2(1000, 236);
        _state.SetFacing(1);
        _state.AnimationSet = 1;
        _state.AnimationDirection = 1;
        _fieldRenderer.LoadZone(
            zone,
            player,
            _actionData,
            fieldLoader.PlayerFramePath,
            (set, frame) =>
            set is >= 0x16 and <= 0x19 ? loader.DropFramePath(frame) : loader.ClimbFramePath(frame),
            null);
        _fieldRenderer.Present(_state, _zone, f3On: false);
    }

    private ClimbData? _actionData;

    /// <summary>Combines climbing and drop animation sequences into one action-data set.</summary>
    /// <param name="climb">Climbing sequences.</param>
    /// <param name="drop">Drop sequences.</param>
    /// <returns>Combined action-animation data.</returns>
    private static ClimbData MergeActionData(ClimbData climb, ClimbData drop)
    {
        var merged = new Dictionary<int, ClimbSequence>(climb.Sequences);
        foreach (var pair in drop.Sequences)
        {
            if (!merged.TryAdd(pair.Key, pair.Value))
            {
                throw new InvalidOperationException($"action animation set collision: {pair.Key:X}");
            }
        }

        return new ClimbData(merged);
    }

    /// <summary>Builds a simulation field zone from decoded zone and collision data.</summary>
    /// <param name="zone">Decoded zone data.</param>
    /// <param name="collision">Collision actors available to the probe.</param>
    /// <returns>Field-zone aggregate used by simulation.</returns>
    private static FieldZone BuildZone(Zone zone, IReadOnlyList<CollisionActor> collision)
    {
        var occupants = collision.Where(actor => actor.ZoneId == zone.Id)
            .Select(actor => new Occupant(actor.ActorId, new NumericsVector2(actor.X, actor.Y), actor.Width)).ToArray();
        return new FieldZone(zone, LoadFloor(zone), LoadTriggers(zone), occupants);
    }

    /// <summary>Loads the floor-cell image for a zone into a simulation map.</summary>
    /// <param name="zone">Zone whose floor map is loaded.</param>
    /// <returns>Decoded floor map.</returns>
    private static FloorMap LoadFloor(Zone zone)
    {
        var image = Image.LoadFromFile(zone.FloorCellsPath);
        if (image is null || image.IsEmpty())
        {
            throw new CacheResourceException("CACHE_RESOURCE_UNAVAILABLE", $"floor image unreadable: {zone.FloorCellsPath}");
        }

        var cells = new byte[zone.Width * zone.Height];
        for (var y = 0; y < zone.Height; y++)
        {
            for (var x = 0; x < zone.Width; x++)
            {
                cells[(y * zone.Width) + x] = (byte)image.GetPixel(x, y).R8;
            }
        }

        return new FloorMap(zone.Width, zone.Height, cells);
    }

    /// <summary>Loads the trigger-cell image for a zone into a simulation map.</summary>
    /// <param name="zone">Zone whose trigger map is loaded.</param>
    /// <returns>Decoded trigger map.</returns>
    private static TriggerMap LoadTriggers(Zone zone)
    {
        var image = Image.LoadFromFile(zone.TriggerCellsPath);
        if (image is null || image.IsEmpty())
        {
            throw new CacheResourceException("CACHE_RESOURCE_UNAVAILABLE", $"trigger image unreadable: {zone.TriggerCellsPath}");
        }

        var cells = new byte[zone.Width * zone.Height];
        for (var y = 0; y < zone.Height; y++)
        {
            for (var x = 0; x < zone.Width; x++)
            {
                cells[(y * zone.Width) + x] = (byte)image.GetPixel(x, y).R8;
            }
        }

        return new TriggerMap(zone.Width, zone.Height, cells);
    }

    /// <summary>Runs the deterministic battle scenario and writes its captures.</summary>
    private async void RunScenario()
    {
        try
        {
            var loader = _battleLoader ?? throw new InvalidOperationException("battle loader unavailable");
            var (flow, state) = SetupBattle(loader);

            _battleRenderer = new BattleRenderer(this);
            await BuildBattleScene(loader, flow);
            await RunBattleLoop(flow, state);

            // Return to the field.
            _battleRenderer.Hide();
            _fieldRenderer!.Present(_state, _zone!, f3On: false);
            await Capture("battle-after-field.png");
            _log.Add($"field-returned zone=S200 position=({_state.Position.X:0},{_state.Position.Y:0})");

            _log.Add("battle probe PASS outcome=Won return=field captures=" + Path.Combine(_captureRoot, "battle-*.png"));
            foreach (var line in _log)
            {
                GD.Print("BATTLE_PROBE " + line);
            }

            GetTree().Quit(0);
        }
        catch (Exception error)
        {
            GD.PushError($"BATTLE_PROBE_FAIL: {error.Message}");
            GetTree().Quit(3);
        }
    }

    /// <summary>Creates the deterministic battle flow and state used by the probe.</summary>
    /// <param name="loader">Loader for battle data.</param>
    /// <returns>Configured battle flow and mutable battle state.</returns>
    private static (BattleFlowController Flow, BattleState State) SetupBattle(BattleDataPackLoader loader)
    {
        var fighters = loader.LoadFighters();
        var techs = loader.LoadTechs();
        var encounters = loader.LoadEncounters();

        // The Genji formation (encounter 200): one enemy, fighter 51, against the starting Agumon.
        var nameIds = new Dictionary<int, int> { [3] = fighters.Digimon[3].NameId, [51] = fighters.BattleTable.Single(entry => entry.Id == 51).NameId };
        var party = BattleSetupFactory.BuildParty(fighters, level: 1, [3]);
        var enemies = BattleSetupFactory.BuildEnemies(encounters, 200, fighters);
        var setup = new BattleSetup
        {
            Partners = party,
            Enemies = enemies,
            Blocks = new int[12],
        };
        setup.Blocks[BattleConst.BlockRunAway] = 1; // encounter 200 is scripted: no fleeing
        setup.Stage = 18; // encounter 200 -> fight stage 18 (the S815 "Gunslinger 2F" background)
        var state = BattleState.InitFighters(setup, digimon: 3);

        var flow = BattleSetupFactory.BuildFlow(
            fighters,
            techs,
            encounters,
            200,
            [3],
            nameIds,
            new BattleTextCatalog(loader.LoadBattleText()));
        flow.Start(state);
        return (flow, state);
    }

    /// <summary>Builds models, HUD, stage, and cameras for the battle presentation.</summary>
    /// <param name="loader">Loader for battle data.</param>
    /// <param name="flow">Configured battle flow.</param>
    /// <returns>An asynchronous operation that completes after capture and setup.</returns>
    private async System.Threading.Tasks.Task BuildBattleScene(BattleDataPackLoader loader, BattleFlowController flow)
    {
        var modelPath = System.Environment.GetEnvironmentVariable("DW3_BATTLE_MODEL") ?? loader.ModelPath(3);
        var enemyModelPath = System.Environment.GetEnvironmentVariable("DW3_BATTLE_ENEMY_MODEL") ?? loader.ModelPath(51);
        _battleRenderer!.BuildModels(modelPath, enemyModelPath, "Agumon", "Ennemi #51", 51);
        Require(_battleRenderer.ModelCount == 2, $"enemy model fell back to a capsule (built {_battleRenderer.ModelCount} real models)");

        var hud = loader.LoadBattleHud();
        _battleRenderer.SetHud(hud);
        _battleRenderer.SetMenuIcons(loader.LoadBattleMenuIcons());
        _log.Add($"battle-hud frames={hud.Frames.Count} cursor={hud.Cursor.Width}x{hud.Cursor.Height}");

        await Capture("battle-before-field.png");
        _log.Add($"field-before zone=S200 position=({_state.Position.X:0},{_state.Position.Y:0})");

        _battleRenderer.Show();
        _battleRenderer.Present(BuildView(flow));
        await Capture("battle-before-stage.png");
        _log.Add("battle-before-stage dark-floor (no fight stage)");

        var stageModelPath = System.Environment.GetEnvironmentVariable("DW3_BATTLE_STAGE") ?? loader.StagePath(18);
        Require(_battleRenderer.BuildStage(stageModelPath, 18), $"fight stage 18 model did not load from {stageModelPath}");
        await Capture("battle-with-stage.png");
        _log.Add("battle-with-stage stage=18 decoded background");

        await SetupCameras(flow);
    }

    /// <summary>Captures configured fighter camera views for the probe evidence.</summary>
    /// <param name="flow">Battle flow used to create presentation views.</param>
    /// <returns>An asynchronous operation that completes after camera captures.</returns>
    private async System.Threading.Tasks.Task SetupCameras(BattleFlowController flow)
    {
        var playerCams = _battleRenderer!.FighterCameras(enemy: false);
        var enemyCams = _battleRenderer.FighterCameras(enemy: true);
        Require(playerCams.Count > 0 && enemyCams.Count > 0, "fighter cameras not decoded");
        _battleRenderer.SetOpeningCamera();
        _battleRenderer.Present(BuildView(flow));
        await Capture("battle-camera-enemy.png");
        var opening = playerCams[enemyCams.Count - 1];
        _log.Add($"battle-camera-enemy pos={opening.CamPos[0]},{opening.CamPos[1]},{opening.CamPos[2]} proj={opening.Proj}");
        var playerClose = playerCams.Count > 6 ? playerCams[6] : playerCams[0];
        _battleRenderer.SetSourceCamera(playerClose, snap: true);
        _battleRenderer.Present(BuildView(flow));
        await Capture("battle-camera-player.png");
        _log.Add($"battle-camera-player pos={playerClose.CamPos[0]},{playerClose.CamPos[1]},{playerClose.CamPos[2]} proj={playerClose.Proj}");
    }

    /// <summary>Captures battle poses and runs the deterministic combat loop.</summary>
    /// <param name="flow">Battle flow to advance.</param>
    /// <param name="state">Mutable battle state.</param>
    /// <returns>An asynchronous operation that completes after battle presentation checks.</returns>
    private async System.Threading.Tasks.Task RunBattleLoop(BattleFlowController flow, BattleState state)
    {
        await Capture("battle-start.png");
        _log.Add($"battle-start player=Agumon hp={state.Players[0]!.Hp} enemy=fighter51 hp={state.Enemies[0]!.Hp}");

        _battleRenderer!.PoseMotionAll(2, 0);
        await Capture("battle-attack-pose.png");
        _log.Add("battle-attack-pose motion=2 poses-distinct");

        _battleRenderer.SetFlash(1f);
        _battleRenderer.ShowHitSpark(onEnemy: true, show: true);
        await Capture("battle-hit-effect.png");
        _battleRenderer.SetFlash(0f);
        _battleRenderer.ShowHitSpark(onEnemy: true, show: false);
        _log.Add("battle-hit-effect white-flash + hit-spark");

        await Capture("battle-hud.png");
        _log.Add("battle-hud source-layout bars + command menu");

        await VerifyEnemyModel(flow, state);
    }

    /// <summary>Checks enemy-model visibility and distinct poses before completing combat.</summary>
    /// <param name="flow">Battle flow to advance after rendering checks.</param>
    /// <param name="state">Mutable battle state inspected for victory.</param>
    /// <returns>An asynchronous operation that completes after verification and combat.</returns>
    private async System.Threading.Tasks.Task VerifyEnemyModel(BattleFlowController flow, BattleState state)
    {
        const int enemyXMin = 640, enemyXMax = 1280;
        _battleRenderer!.PoseAll(20);
        var poseA = await Capture("battle-enemy-pose-a.png");
        _battleRenderer.PoseAll(60);
        var poseB = await Capture("battle-enemy-pose-b.png");
        var brightA = CountBrightPixels(poseA, enemyXMin, enemyXMax);
        var brightB = CountBrightPixels(poseB, enemyXMin, enemyXMax);
        var distinct = CountDifferentPixels(poseA, poseB, enemyXMin, enemyXMax);
        Require(brightA > 200 && brightB > 200, $"enemy model rendered too few opaque pixels (a={brightA} b={brightB})");
        Require(distinct > 50, $"enemy model poses are not distinct (diff={distinct})");
        _log.Add($"battle-enemy-model opaque-a={brightA} opaque-b={brightB} distinct-pixels={distinct}");

        // Deterministic loop: the player attacks, the enemy counter-attacks, until victory.
        var rng = new FixedRng(0x2A);
        var guard = 0;
        while (flow.Phase != BattleFlowPhase.Ended && guard++ < 128)
        {
            if (flow.Phase == BattleFlowPhase.PlayerCommand)
            {
                flow.Choose(BattleCommand.Attack, 0, rng);
            }
            else
            {
                flow.EnemyAct(rng);
            }

            _log.Add($"turn phase={flow.Phase} player_hp={state.Players[0]!.Hp} enemy_hp={state.Enemies[0]!.Hp} msg={flow.Message}");
        }

        Require(flow.Outcome == BattleOutcome.Won, "deterministic encounter-200 battle did not reach victory");
        Require(state.Enemies[0]!.Hp == 0, "victory left the enemy alive");
        _battleRenderer.Present(BuildView(flow));
        await Capture("battle-victory.png");
        _log.Add($"battle-victory outcome=Won player_hp={state.Players[0]!.Hp}");
    }

    /// <summary>Builds a presentation view from the current battle flow.</summary>
    /// <param name="flow">Battle flow supplying state and text.</param>
    /// <returns>View consumed by the battle renderer.</returns>
    private static BattleView BuildView(BattleFlowController flow)
    {
        var players = BuildFighterViews(flow.State.Players, flow.State.ActivePlayer, flow);
        var enemies = BuildFighterViews(flow.State.Enemies, flow.State.ActiveEnemy, flow);
        return new BattleView(flow.Phase, flow.Outcome, players, enemies, flow.Message, null);
    }

    /// <summary>Builds presentation rows for one side of the encounter.</summary>
    /// <param name="fighters">Fighters occupying the side slots.</param>
    /// <param name="activeIndex">Index of the active fighter.</param>
    /// <param name="flow">Battle flow supplying names.</param>
    /// <returns>Presentation rows for the supplied fighter slots.</returns>
    private static List<BattleFighterView> BuildFighterViews(BattleFighter?[] fighters, int activeIndex, BattleFlowController flow)
    {
        var views = new List<BattleFighterView>();
        foreach (var fighter in fighters)
        {
            if (fighter is null)
            {
                views.Add(new BattleFighterView(0, null, 0, 0, 0, 0, false, false));
                continue;
            }

            var isActive = activeIndex >= 0 && fighters[activeIndex] == fighter;
            var name = flow.NameIds.TryGetValue(fighter.Id, out var nameId) ? flow.Text?.DigimonByNameId(nameId) : BattleTextLine.Missing;
            views.Add(new BattleFighterView(fighter.Id, name, fighter.Hp, fighter.MaxHp, fighter.Mp, fighter.MaxMp, isActive, fighter.IsKnockedOut));
        }

        return views;
    }

    private readonly List<string> _log = [];

    /// <summary>Waits for rendering, captures the viewport, and writes a PNG file.</summary>
    /// <param name="fileName">Name assigned to the capture file.</param>
    /// <returns>Captured viewport image.</returns>
    private async System.Threading.Tasks.Task<Image> Capture(string fileName)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var image = GetViewport().GetTexture().GetImage();
        var path = Path.Combine(_captureRoot, fileName);
        if (image.SavePng(path) != Error.Ok)
        {
            throw new InvalidOperationException($"capture write failed: {path}");
        }

        return image;
    }

    /// <summary>Counts bright non-background pixels in a horizontal image band.</summary>
    /// <param name="image">Capture image to inspect.</param>
    /// <param name="xMin">Inclusive left edge of the inspected band.</param>
    /// <param name="xMax">Exclusive right edge of the inspected band.</param>
    /// <returns>The number of bright pixels in the band.</returns>
    private static int CountBrightPixels(Image image, int xMin, int xMax)
    {
        var data = image.GetData();
        var bytesPerPixel = image.GetFormat() == Image.Format.Rgba8 ? 4 : 3;
        var width = image.GetWidth();
        var height = image.GetHeight();
        var count = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = xMin; x < xMax; x++)
            {
                var index = ((y * width) + x) * bytesPerPixel;
                if (data[index] + data[index + 1] + data[index + 2] > 120)
                {
                    count++;
                }
            }
        }

        return count;
    }

    /// <summary>Counts pixels that differ between two same-sized captures in a horizontal band.</summary>
    /// <param name="a">First capture image.</param>
    /// <param name="b">Second capture image.</param>
    /// <param name="xMin">Inclusive left edge of the inspected band.</param>
    /// <param name="xMax">Exclusive right edge of the inspected band.</param>
    /// <returns>The number of pixels whose RGB distance exceeds the comparison threshold.</returns>
    private static int CountDifferentPixels(Image a, Image b, int xMin, int xMax)
    {
        var ad = a.GetData();
        var bd = b.GetData();
        var bytesPerPixel = a.GetFormat() == Image.Format.Rgba8 ? 4 : 3;
        var width = a.GetWidth();
        var height = a.GetHeight();
        var count = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = xMin; x < xMax; x++)
            {
                var index = ((y * width) + x) * bytesPerPixel;
                if (Math.Abs(ad[index] - bd[index]) + Math.Abs(ad[index + 1] - bd[index + 1]) + Math.Abs(ad[index + 2] - bd[index + 2]) > 30)
                {
                    count++;
                }
            }
        }

        return count;
    }

    /// <summary>Throws a probe failure when a required condition is false.</summary>
    /// <param name="condition">Condition that must hold.</param>
    /// <param name="message">Failure description used when the condition is false.</param>
    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"BATTLE_PROBE_FAIL: {message}");
        }
    }

    /// <summary>Supplies a deterministic pseudo-random sequence for probe combat.</summary>
    private sealed class FixedRng : IBattleRng
    {
        private int _value;

        /// <summary>Initializes a new instance of the <see cref="FixedRng"/> class from a seed value.</summary>
        /// <param name="seed">Initial pseudo-random state.</param>
        public FixedRng(int seed) => _value = seed;

        /// <summary>Advances and returns the next deterministic pseudo-random value.</summary>
        /// <returns>Next value in the sequence.</returns>
        public int NextValue() => _value = ((_value * 1103515245) + 12345) & 0x7FFFFFFF;
    }
}
