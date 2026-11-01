using Godot;
using Dw3.Data;
using Dw3Production.Conversion;
using Dw3Production.Simulation;
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

    public override void _ExitTree()
    {
        _fieldLoader?.Dispose();
        _fieldLoader = null;
        _animationLoader?.Dispose();
        _animationLoader = null;
        _battleLoader?.Dispose();
        _battleLoader = null;
    }

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
        _fieldRenderer.LoadZone(zone, player, _actionData, fieldLoader.PlayerFramePath, (set, frame) =>
            set is >= 0x16 and <= 0x19 ? loader.DropFramePath(frame) : loader.ClimbFramePath(frame), null);
        _fieldRenderer.Present(_state, _zone!, f3On: false);
    }

    private ClimbData? _actionData;

    private static ClimbData MergeActionData(ClimbData climb, ClimbData drop)
    {
        var merged = new Dictionary<int, ClimbSequence>(climb.Sequences);
        foreach (var pair in drop.Sequences)
            if (!merged.TryAdd(pair.Key, pair.Value)) throw new InvalidOperationException($"action animation set collision: {pair.Key:X}");
        return new ClimbData(merged);
    }

    private static FieldZone BuildZone(Zone zone, IReadOnlyList<CollisionActor> collision)
    {
        var occupants = collision.Where(actor => actor.ZoneId == zone.Id)
            .Select(actor => new Occupant(actor.ActorId, new NumericsVector2(actor.X, actor.Y), actor.Width)).ToArray();
        return new FieldZone(zone, LoadFloor(zone), LoadTriggers(zone), occupants);
    }

    private static FloorMap LoadFloor(Zone zone)
    {
        var image = Image.LoadFromFile(zone.FloorCellsPath);
        if (image is null || image.IsEmpty()) throw new CacheResourceException("CACHE_RESOURCE_UNAVAILABLE", $"floor image unreadable: {zone.FloorCellsPath}");
        var cells = new byte[zone.Width * zone.Height];
        for (var y = 0; y < zone.Height; y++)
            for (var x = 0; x < zone.Width; x++)
                cells[y * zone.Width + x] = (byte)image.GetPixel(x, y).R8;
        return new FloorMap(zone.Width, zone.Height, cells);
    }

    private static TriggerMap LoadTriggers(Zone zone)
    {
        var image = Image.LoadFromFile(zone.TriggerCellsPath);
        if (image is null || image.IsEmpty()) throw new CacheResourceException("CACHE_RESOURCE_UNAVAILABLE", $"trigger image unreadable: {zone.TriggerCellsPath}");
        var cells = new byte[zone.Width * zone.Height];
        for (var y = 0; y < zone.Height; y++)
            for (var x = 0; x < zone.Width; x++)
                cells[y * zone.Width + x] = (byte)image.GetPixel(x, y).R8;
        return new TriggerMap(zone.Width, zone.Height, cells);
    }

    private async void RunScenario()
    {
        try
        {
            var loader = _battleLoader ?? throw new InvalidOperationException("battle loader unavailable");
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

            var flow = BattleSetupFactory.BuildFlow(fighters, techs, encounters, 200, [3], nameIds,
                new BattleTextCatalog(loader.LoadBattleText()), level: 1);
            flow.Start(state);

            _battleRenderer = new BattleRenderer(this);
            var modelPath = System.Environment.GetEnvironmentVariable("DW3_BATTLE_MODEL")
                ?? loader.ModelPath(3);
            var enemyModelPath = System.Environment.GetEnvironmentVariable("DW3_BATTLE_ENEMY_MODEL")
                ?? loader.ModelPath(51);
            _battleRenderer.BuildModels(modelPath, enemyModelPath, "Agumon", "Ennemi #51", 51);
            Require(_battleRenderer.ModelCount == 2,
                $"enemy model fell back to a capsule (built {_battleRenderer.ModelCount} real models)");

            // Battle-HUD sprites (HP-bar window frames, technique
            // gauge, cursor) decoded from the disc, drawn instead of the flat procedural panel.
            var hud = loader.LoadBattleHud();
            _battleRenderer.SetHud(hud);
            _battleRenderer.SetMenuIcons(loader.LoadBattleMenuIcons());
            _log.Add($"battle-hud frames={hud.Frames.Count} cursor={hud.Cursor.Width}x{hud.Cursor.Height}");

            // Field before the battle (the battle layer is still hidden).
            await Capture("battle-before-field.png");
            _log.Add($"field-before zone=S200 position=({_state.Position.X:0},{_state.Position.Y:0})");

            _battleRenderer.Show();
            _battleRenderer.Present(BuildView(flow));
            // Capture the battle before the fight stage is loaded.
            await Capture("battle-before-stage.png");
            _log.Add("battle-before-stage dark-floor (no fight stage)");

            // Decode and draw the real fight stage (stage 18) as the background.
            var stageModelPath = System.Environment.GetEnvironmentVariable("DW3_BATTLE_STAGE")
                ?? loader.StagePath(18);
            Require(_battleRenderer.BuildStage(stageModelPath, 18),
                $"fight stage 18 model did not load from {stageModelPath}");
            await Capture("battle-with-stage.png");
            _log.Add("battle-with-stage stage=18 decoded background");

            // The source battle camera. The decoded fighter cameras
            // (the decoded fighter-camera record contract / the decoded battle-camera record contract) drive the close-up on the active fighter.
            var playerCams = _battleRenderer.FighterCameras(enemy: false);
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

            await Capture("battle-start.png");
            _log.Add($"battle-start player=Agumon hp={state.Players[0]!.Hp} enemy=fighter51 hp={state.Enemies[0]!.Hp}");

            // Pose the active model at an attack motion (motion 2 = the
            // non-idle action), proving the decoded motions drive a real attack pose.
            _battleRenderer.PoseMotionAll(2, 0);
            await Capture("battle-attack-pose.png");
            _log.Add($"battle-attack-pose motion=2 poses-distinct");

            // The hit effect: the white flash and the hit spark on the enemy.
            _battleRenderer.SetFlash(1f);
            _battleRenderer.ShowHitSpark(onEnemy: true, show: true);
            await Capture("battle-hit-effect.png");
            _battleRenderer.SetFlash(0f);
            _battleRenderer.ShowHitSpark(onEnemy: true, show: false);
            _log.Add("battle-hit-effect white-flash + hit-spark");

            // The disc-derived HUD (HP bars top-left/top-right, command menu).
            await Capture("battle-hud.png");
            _log.Add("battle-hud source-layout bars + command menu");

            // The enemy model renders on its half of the screen at two distinct
            // poses, with real opaque pixels (never an empty frame). ModelCount == 2 above
            // already rules out a capsule; these two captures rule out an untextured/static model.
            const int enemyXMin = 640, enemyXMax = 1280;
            _battleRenderer.PoseAll(20);
            var poseA = await Capture("battle-enemy-pose-a.png");
            _battleRenderer.PoseAll(60);
            var poseB = await Capture("battle-enemy-pose-b.png");
            var brightA = CountBrightPixels(poseA, enemyXMin, enemyXMax);
            var brightB = CountBrightPixels(poseB, enemyXMin, enemyXMax);
            var distinct = CountDifferentPixels(poseA, poseB, enemyXMin, enemyXMax);
            Require(brightA > 200 && brightB > 200,
                $"enemy model rendered too few opaque pixels (a={brightA} b={brightB})");
            Require(distinct > 50, $"enemy model poses are not distinct (diff={distinct})");
            _log.Add($"battle-enemy-model opaque-a={brightA} opaque-b={brightB} distinct-pixels={distinct}");

            // Deterministic loop: the player attacks, the enemy counter-attacks, until victory.
            var rng = new FixedRng(0x2A);
            var guard = 0;
            while (flow.Phase != BattleFlowPhase.Ended && guard++ < 128)
            {
                if (flow.Phase == BattleFlowPhase.PlayerCommand) flow.Choose(BattleCommand.Attack, 0, rng);
                else flow.EnemyAct(rng);
                _log.Add($"turn phase={flow.Phase} player_hp={state.Players[0]!.Hp} enemy_hp={state.Enemies[0]!.Hp} msg={flow.Message}");
            }
            Require(flow.Outcome == BattleOutcome.Won, "deterministic encounter-200 battle did not reach victory");
            Require(state.Enemies[0]!.Hp == 0, "victory left the enemy alive");
            _battleRenderer.Present(BuildView(flow));
            await Capture("battle-victory.png");
            _log.Add($"battle-victory outcome=Won player_hp={state.Players[0]!.Hp}");

            // Return to the field.
            _battleRenderer.Hide();
            _fieldRenderer!.Present(_state, _zone!, f3On: false);
            await Capture("battle-after-field.png");
            _log.Add($"field-returned zone=S200 position=({_state.Position.X:0},{_state.Position.Y:0})");

            _log.Add("battle probe PASS outcome=Won return=field captures=" + Path.Combine(_captureRoot, "battle-*.png"));
            foreach (var line in _log) GD.Print("BATTLE_PROBE " + line);
            GetTree().Quit(0);
        }
        catch (Exception error)
        {
            GD.PushError($"BATTLE_PROBE_FAIL: {error.Message}");
            GetTree().Quit(3);
        }
    }

    private static BattleView BuildView(BattleFlowController flow)
    {
        var players = new List<BattleFighterView>();
        foreach (var fighter in flow.State.Players)
        {
            if (fighter is null) { players.Add(new BattleFighterView(0, null, 0, 0, 0, 0, false, false)); continue; }
            players.Add(new BattleFighterView(fighter.Id, flow.NameIds.TryGetValue(fighter.Id, out var nameId) ? flow.Text?.DigimonByNameId(nameId) : BattleTextLine.Missing,
                fighter.Hp, fighter.MaxHp, fighter.Mp, fighter.MaxMp,
                flow.State.ActivePlayer >= 0 && flow.State.Players[flow.State.ActivePlayer] == fighter, fighter.IsKnockedOut));
        }
        var enemies = new List<BattleFighterView>();
        foreach (var fighter in flow.State.Enemies)
        {
            if (fighter is null) { enemies.Add(new BattleFighterView(0, null, 0, 0, 0, 0, false, false)); continue; }
            enemies.Add(new BattleFighterView(fighter.Id, flow.NameIds.TryGetValue(fighter.Id, out var nameId) ? flow.Text?.DigimonByNameId(nameId) : BattleTextLine.Missing,
                fighter.Hp, fighter.MaxHp, fighter.Mp, fighter.MaxMp,
                flow.State.ActiveEnemy >= 0 && flow.State.Enemies[flow.State.ActiveEnemy] == fighter, fighter.IsKnockedOut));
        }
        return new BattleView(flow.Phase, flow.Outcome, players, enemies, flow.Message, null);
    }

    private readonly List<string> _log = [];

    private async System.Threading.Tasks.Task<Image> Capture(string fileName)
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        var image = GetViewport().GetTexture().GetImage();
        var path = Path.Combine(_captureRoot, fileName);
        if (image.SavePng(path) != Error.Ok) throw new InvalidOperationException($"capture write failed: {path}");
        return image;
    }

    /// <summary>Counts the bright (non-background) pixels in the horizontal band [xMin, xMax),
    /// the "opaque pixels" evidence that a textured model is actually rendered.</summary>
    private static int CountBrightPixels(Image image, int xMin, int xMax)
    {
        var data = image.GetData();
        var bytesPerPixel = image.GetFormat() == Image.Format.Rgba8 ? 4 : 3;
        var width = image.GetWidth();
        var height = image.GetHeight();
        var count = 0;
        for (var y = 0; y < height; y++)
            for (var x = xMin; x < xMax; x++)
            {
                var index = (y * width + x) * bytesPerPixel;
                if (data[index] + data[index + 1] + data[index + 2] > 120) count++;
            }
        return count;
    }

    /// <summary>Counts pixels that differ between two same-sized captures in [xMin, xMax).</summary>
    private static int CountDifferentPixels(Image a, Image b, int xMin, int xMax)
    {
        var ad = a.GetData();
        var bd = b.GetData();
        var bytesPerPixel = a.GetFormat() == Image.Format.Rgba8 ? 4 : 3;
        var width = a.GetWidth();
        var height = a.GetHeight();
        var count = 0;
        for (var y = 0; y < height; y++)
            for (var x = xMin; x < xMax; x++)
            {
                var index = (y * width + x) * bytesPerPixel;
                if (Math.Abs(ad[index] - bd[index]) + Math.Abs(ad[index + 1] - bd[index + 1]) + Math.Abs(ad[index + 2] - bd[index + 2]) > 30)
                    count++;
            }
        return count;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException($"BATTLE_PROBE_FAIL: {message}");
    }

    /// <summary>A fixed, deterministic RNG for the probe (never returns the same roll twice in a row
    /// unless asked).</summary>
    private sealed class FixedRng : BattleRng
    {
        private int _value;
        public FixedRng(int seed) => _value = seed;
        public int Next() => _value = (_value * 1103515245 + 12345) & 0x7FFFFFFF;
    }
}
