using System.Globalization;
using Dw3Production.Presentation;
using Godot;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Reads one production tick of an entrance call from the live battle scene.</summary>
internal static class BattleEntranceTickReader
{
    /// <summary>Builds the sample of the tick just played.</summary>
    /// <param name="host">Live battle probe host.</param>
    /// <param name="call">Entrance call, possibly already released.</param>
    /// <param name="tick">Tick since the call started.</param>
    /// <param name="enemy">Whether the enemy side enters.</param>
    /// <returns>The observed sample.</returns>
    public static BattleEntranceTickSample Read(IBattleProbeHost host, BattleEntranceCall call, int tick, bool enemy)
    {
        BattleView? view = host.BuildBattleView();
        var scene = host.Renderer.ActorScene;
        var side = enemy ? view?.Enemies : view?.Players;
        string message = view is { MessageVisible: true } shown ? shown.MessageText?.Second?.Debug ?? shown.MessageText?.Debug ?? shown.Message : string.Empty;
        Vector3 camera = host.Renderer.CameraForProbe.GlobalPosition;
        var music = host.Renderer.MusicStateForProbe;
        return new BattleEntranceTickSample(
            tick,
            call.Phase.ToString(),
            call.Sequence.Frames,
            call.AfterFrames,
            call.Sequence.FlashLevel,
            enemy ? host.RenderedEnemyId : host.RenderedPlayerId,
            side?.FirstOrDefault(fighter => fighter.Active).Id ?? -1,
            scene.FighterModel(enemy)?.Motion?.Motion ?? -1,
            scene.FighterModel(!enemy)?.Root.Visible == true,
            message,
            scene.Active,
            string.Create(CultureInfo.InvariantCulture, $"{camera.X:0.####},{camera.Y:0.####},{camera.Z:0.####}"),
            host.Flow.Phase.ToString(),
            host.Renderer.StageIdForProbe,
            string.Create(CultureInfo.InvariantCulture, $"{music.Id:X8}:{music.Volume:0.###}"),
            host.Renderer.EntranceCuePlaysForProbe);
    }

    /// <summary>Returns the white level the sprite surface actually draws.</summary>
    /// <param name="host">Live battle probe host.</param>
    /// <returns>The drawn level from 0 to 1.</returns>
    public static float DrawnFlash(IBattleProbeHost host) =>
        host.Renderer.SequencePlayback.DiagnosticSprites?.Surface is BattleSpriteCanvas canvas && canvas.Flash.Visible ? canvas.Flash.Color.R : 0f;
}
