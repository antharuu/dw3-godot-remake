using System.Text.Json;
using Dw3Production.Diagnostics.Field;
using Dw3Production.Presentation;
using Dw3Production.Simulation;
using Godot;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>
/// Plays an accepted switch's entrance through the production battle tick and presentation, frame by frame, and
/// checks the source timeline on the real scene: the outgoing model stays until full white, the opponent is hidden
/// from frame 30, the flow holds the enemy turn, the incoming model replaces it at full white in its entrance motion,
/// and the camera, idle and opponent visibility are restored at the end. Captures are written for the lead to read.
/// </summary>
internal sealed class BattleEntranceSwitchProof
{
    /// <summary>Frames captured as images, chosen inside each entrance stage.</summary>
    private static readonly int[] CaptureFrames = [20, 45, 110, 126, 150, 250, 309];

    private readonly IBattleProbeHost _host;
    private readonly string _captureRoot;
    private readonly List<object> _samples = [];

    /// <summary>Initializes a new instance of the <see cref="BattleEntranceSwitchProof"/> class.</summary>
    /// <param name="host">Live battle probe host.</param>
    /// <param name="captureRoot">Private capture directory.</param>
    public BattleEntranceSwitchProof(IBattleProbeHost host, string captureRoot)
    {
        _host = host;
        _captureRoot = captureRoot;
    }

    /// <summary>Plays the entrance started by the switch and asserts its observable stages.</summary>
    /// <param name="outgoing">Fighter id rendered before the switch.</param>
    /// <param name="incoming">Fighter id chosen by the switch.</param>
    /// <returns>The number of source frames the entrance held the flow.</returns>
    public async Task<int> RunAsync(int outgoing, int incoming)
    {
        var entrance = _host.ActorEntrance;
        ProbeRequirement.Require(entrance is { Enemy: false }, "switch did not start a player entrance");
        ProbeRequirement.Require(_host.RenderedPlayerId == outgoing, $"switch replaced the model before the entrance rendered={_host.RenderedPlayerId}");
        int playerHp = _host.Flow.State.ActivePlayerFighter?.Hp ?? -1;
        int frame = entrance?.Frames ?? 0;
        while (_host.ActorEntrance is { } playing)
        {
            ProbeRequirement.Require(playing.Frames < 400, "entrance did not complete within 400 frames");
            _host.TickTestSimulation();
            frame = playing.Frames;
            _host.Renderer.Present(_host.BuildBattleView());
            CheckFrame(playing, frame, outgoing, incoming);
            if (CaptureFrames.Contains(frame))
            {
                await BattleProbeCapture.CaptureRawAsync(_host, _captureRoot, $"entrance-{frame:000}.png");
            }
        }

        ProbeRequirement.Require(frame == 309, $"entrance held the flow for {frame} frames, expected 309");
        ProbeRequirement.Require(_host.Flow.State.ActivePlayerFighter?.Hp == playerHp, "the enemy acted during the entrance");
        CheckEnd(incoming);
        for (int i = 0; i < 60; i++)
        {
            _host.Renderer.Present(_host.BuildBattleView());
        }

        await BattleProbeCapture.CaptureRawAsync(_host, _captureRoot, "entrance-return.png");
        await File.WriteAllTextAsync(Path.Combine(_captureRoot, "entrance-frames.json"), JsonSerializer.Serialize(_samples));
        return frame;
    }

    /// <summary>Checks one played frame against the source stage it belongs to.</summary>
    /// <param name="entrance">Entrance after the frame.</param>
    /// <param name="frame">One-based frame.</param>
    /// <param name="outgoing">Outgoing fighter id.</param>
    /// <param name="incoming">Incoming fighter id.</param>
    private void CheckFrame(BattleEntranceSequence entrance, int frame, int outgoing, int incoming)
    {
        bool enemyVisible = _host.Renderer.ActorScene.FighterModel(enemy: true)?.Root.Visible == true;
        int rendered = _host.RenderedPlayerId;
        int motion = _host.Renderer.ActorScene.FighterModel(enemy: false)?.Motion?.Motion ?? -1;
        BattleView? view = _host.BuildBattleView();
        bool? message = view?.MessageVisible;
        int hudPlayer = view?.Players.FirstOrDefault(fighter => fighter.Active).Id ?? -1;
        ProbeRequirement.Require(hudPlayer == (entrance.Swapped ? incoming : outgoing), $"frame {frame}: HUD active={hudPlayer} swapped={entrance.Swapped}");
        float drawn = _host.Renderer.SequencePlayback.DiagnosticSprites?.Surface is BattleSpriteCanvas canvas && canvas.Flash.Visible ? canvas.Flash.Color.R : 0f;
        ProbeRequirement.Require(Math.Abs(drawn - (entrance.FlashLevel / 255f)) < 0.002f, $"frame {frame}: drawn flash={drawn} expected={entrance.FlashLevel}");
        _samples.Add(new { frame, phase = entrance.Phase.ToString(), flash = entrance.FlashLevel, rendered, hudPlayer, motion, enemyVisible, message });
        ProbeRequirement.Require(_host.Flow.Phase == BattleFlowPhase.EnemyTurn, $"frame {frame}: flow left the held enemy turn phase={_host.Flow.Phase}");
        ProbeRequirement.Require(message != true || entrance.IsComplete, $"frame {frame}: message shown during the entrance");
        ProbeRequirement.Require(enemyVisible == (frame < BattleEntranceSequence.HideOpponentFrame || entrance.IsComplete), $"frame {frame}: enemy visibility={enemyVisible}");
        ProbeRequirement.Require(
            entrance.Swapped ? rendered == incoming : rendered == outgoing,
            $"frame {frame}: rendered={rendered} swapped={entrance.Swapped}");
        ProbeRequirement.Require(!entrance.Swapped || frame >= 125, $"frame {frame}: model swapped before full white");
        ProbeRequirement.Require(frame != 126 || motion == BattleEntranceSequence.EntranceMotion, $"frame {frame}: incoming motion={motion}");
    }

    /// <summary>Checks the final state after the flow is released.</summary>
    /// <param name="incoming">Incoming fighter id.</param>
    private void CheckEnd(int incoming)
    {
        var fighter = _host.Flow.State.ActivePlayerFighter;
        var model = _host.Renderer.ActorScene.FighterModel(enemy: false);
        int expectedIdle = fighter is null ? -1 : BattleWeakIdleRule.Select(fighter.Hp, 0, fighter.MaxHp);
        ProbeRequirement.Require(_host.RenderedPlayerId == incoming, $"entrance ended on rendered={_host.RenderedPlayerId}");
        ProbeRequirement.Require(model?.Motion?.IdleMotion == expectedIdle, $"entrance idle={model?.Motion?.IdleMotion} expected={expectedIdle}");
        ProbeRequirement.Require(_host.Renderer.ActorScene.FighterModel(enemy: true)?.Root.Visible == true, "opponent stayed hidden after the entrance");
        ProbeRequirement.Require(!_host.Renderer.ActorScene.Active, "actor transition kept camera ownership");
        Vector3 camera = _host.Renderer.CameraForProbe.GlobalPosition;
        _samples.Add(new { end = true, idle = expectedIdle, camera = $"{camera.X:0.###},{camera.Y:0.###},{camera.Z:0.###}" });
    }
}
