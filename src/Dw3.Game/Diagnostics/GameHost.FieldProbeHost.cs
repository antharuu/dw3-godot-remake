using Dw3Production.Conversion;
using Dw3Production.Diagnostics.Field;
using Dw3Production.Input;
using Dw3Production.Logging;
using Dw3Production.Presentation;
using Dw3Production.Simulation;
using Godot;
using NumericsVector2 = System.Numerics.Vector2;

namespace Dw3Production;

/// <summary>
/// Exposes the running session to the scripted field probes of <c>Diagnostics/Field</c> through
/// <see cref="IFieldProbeHost"/>. Every member forwards to the live host state; the probes
/// themselves live outside this class.
/// </summary>
public partial class GameHost : IFieldProbeHost
{
    /// <inheritdoc/>
    GameState IFieldProbeHost.State => _state;

    /// <inheritdoc/>
    InputAdapter IFieldProbeHost.Input => _input;

    /// <inheritdoc/>
    WarpController IFieldProbeHost.Warp => _warp;

    /// <inheritdoc/>
    DropController IFieldProbeHost.Drop => _drop;

    /// <inheritdoc/>
    QuietLog IFieldProbeHost.Log => _log;

    /// <inheritdoc/>
    FieldRenderer IFieldProbeHost.Renderer => Renderer;

    /// <inheritdoc/>
    FieldZone? IFieldProbeHost.ActiveZone => _currentZone;

    /// <inheritdoc/>
    FieldZone IFieldProbeHost.CurrentZone => CurrentZone;

    /// <inheritdoc/>
    PlayerData? IFieldProbeHost.PlayerData => _playerData;

    /// <inheritdoc/>
    WaterData? IFieldProbeHost.Water => _water;

    /// <inheritdoc/>
    NpcData? IFieldProbeHost.Npc => _npc;

    /// <inheritdoc/>
    FloorMap? IFieldProbeHost.Floor => _floor;

    /// <inheritdoc/>
    IReadOnlyList<Occupant> IFieldProbeHost.Occupants => _occupants;

    /// <inheritdoc/>
    int IFieldProbeHost.ZoneDecodeCount => ZoneCache.DecodeCount;

    /// <inheritdoc/>
    FieldZone IFieldProbeHost.GetOrLoadZone(string zoneId) => GetOrLoadZone(zoneId);

    /// <inheritdoc/>
    void IFieldProbeHost.ApplyScene(FieldZone zone) => ApplyScene(zone);

    /// <inheritdoc/>
    void IFieldProbeHost.SimulateStep(Action<MovementResult> onMovementRejection) => SimulateStep(onMovementRejection);

    /// <inheritdoc/>
    void IFieldProbeHost.TickTestSimulation() => TickTestSimulation();

    /// <inheritdoc/>
    bool IFieldProbeHost.TryTestMove(SemanticDirection direction) => TryTestMove(direction);

    /// <inheritdoc/>
    void IFieldProbeHost.SetTestHeldDirection(SemanticDirection direction) => SetTestHeldDirection(direction);

    /// <inheritdoc/>
    void IFieldProbeHost.ToggleF3() => ToggleF3();

    /// <inheritdoc/>
    void IFieldProbeHost.SimulateWindowFocus(bool focused) =>
        _Notification((int)(focused ? NotificationWMWindowFocusIn : NotificationWMWindowFocusOut));

    /// <inheritdoc/>
    void IFieldProbeHost.RunWarpEdge(string sourceZone, int slot, int facing, string label) => RunWarpEdge(sourceZone, slot, facing, label);

    /// <inheritdoc/>
    string IFieldProbeHost.ResolvePlayerFrame(int frame) => ResolvePlayerFrame(frame);

    /// <inheritdoc/>
    bool IFieldProbeHost.CircleFreeAt(NumericsVector2 position) => CircleFreeAt(position);

    /// <inheritdoc/>
    WarpCallbacks IFieldProbeHost.CreateWarpCallbacks(Action<string, string> warn) =>
        new(ResolveTarget, ApplyScene, QuietLog.Info, warn, StartClimb, StartDrop);

    /// <inheritdoc/>
    SignalAwaiter IFieldProbeHost.NextFrameDrawn() => ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

    /// <inheritdoc/>
    Image IFieldProbeHost.CaptureViewport() => GetViewport().GetTexture().GetImage();

    /// <inheritdoc/>
    Task IFieldProbeHost.CaptureStill(string captureRoot, string fileName) => CaptureStill(captureRoot, fileName);

    /// <inheritdoc/>
    string IFieldProbeHost.PrepareCaptureDirectory() => ProbeOutputRoot(_options.ReferenceDir ?? _dataRoot);

    /// <inheritdoc/>
    string IFieldProbeHost.PrepareVideoDirectory()
    {
        var videoRoot = _options.VideoDir ?? Path.Combine(_options.ReferenceDir ?? _dataRoot, "video");
        Directory.CreateDirectory(videoRoot);
        return videoRoot;
    }

    /// <inheritdoc/>
    void IFieldProbeHost.Quit(int exitCode) => GetTree().Quit(exitCode);

    /// <summary>Runs one simulation step that must not be refused, as scripted probes require.</summary>
    /// <exception cref="InvalidOperationException">Locomotion rejected the held direction.</exception>
    private void TickTestSimulation() =>
        SimulateStep(movement => throw new InvalidOperationException($"test input rejected={movement.Rejection} zone={_state.ZoneId} position={_state.Position}"));

    /// <summary>Holds one direction for a simulation step and reports whether the player moved.</summary>
    /// <param name="direction">Pad direction to hold.</param>
    /// <returns><see langword="true"/> when the position changed.</returns>
    private bool TryTestMove(SemanticDirection direction)
    {
        SetTestHeldDirection(direction);
        var before = _state.Position;
        SimulateStep(_ => { });
        return _state.Position != before;
    }
}
