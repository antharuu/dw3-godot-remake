using Dw3Production.Conversion;
using Dw3Production.Input;
using Dw3Production.Logging;
using Dw3Production.Presentation;
using Dw3Production.Simulation;
using Godot;
using NumericsVector2 = System.Numerics.Vector2;

namespace Dw3Production.Diagnostics.Field;

/// <summary>
/// The part of the running game that a scripted field probe may drive: the live field state, the
/// injected input, the shared fixed-step simulation and the rendered viewport. Probes see nothing
/// else of the host, so they cannot reach battle, dialogue or startup internals.
/// </summary>
internal interface IFieldProbeHost
{
    /// <summary>Gets the live player state that the shared simulation step mutates.</summary>
    GameState State { get; }

    /// <summary>Gets the input adapter through which probes inject keyboard and pad events.</summary>
    InputAdapter Input { get; }

    /// <summary>Gets the exit-offer state machine, read to assert Offer, Rearm and commit counts.</summary>
    WarpController Warp { get; }

    /// <summary>Gets the cliff-drop controller, read to assert when a drop is still running.</summary>
    DropController Drop { get; }

    /// <summary>Gets the host warning log, queried for keys the warp callbacks emitted.</summary>
    QuietLog Log { get; }

    /// <summary>Gets the field renderer whose counters and cue state the probes inspect.</summary>
    /// <exception cref="InvalidOperationException">The renderer has not been created yet.</exception>
    FieldRenderer Renderer { get; }

    /// <summary>Gets the applied field scene, or <see langword="null"/> before the first scene.</summary>
    FieldZone? ActiveZone { get; }

    /// <summary>Gets the applied field scene.</summary>
    /// <exception cref="InvalidOperationException">No scene has been applied yet.</exception>
    FieldZone CurrentZone { get; }

    /// <summary>Gets the player sprite animation table, or <see langword="null"/> before startup loaded it.</summary>
    PlayerData? PlayerData { get; }

    /// <summary>Gets the per-zone map objects (water and StageTile sprites), or <see langword="null"/> when not loaded.</summary>
    WaterData? Water { get; }

    /// <summary>Gets the per-zone NPC sprite records, or <see langword="null"/> when not loaded.</summary>
    NpcData? Npc { get; }

    /// <summary>Gets the floor cells of the applied scene, or <see langword="null"/> before the first scene.</summary>
    FloorMap? Floor { get; }

    /// <summary>Gets the NPC occupancy diamonds that block locomotion in the applied scene.</summary>
    IReadOnlyList<Occupant> Occupants { get; }

    /// <summary>Gets how many zones were decoded from the data pack since startup; a revisit must not add one.</summary>
    int ZoneDecodeCount { get; }

    /// <summary>Gets the decoded connection-icon frames, or <see langword="null"/> before startup loaded them.</summary>
    PlayerIconData? IconData { get; }

    /// <summary>Gets the controller that animates the connection icon and makes it follow the player.</summary>
    PlayerIconController Icon { get; }

    /// <summary>Returns the cached zone, decoding it from the data pack on first use.</summary>
    /// <param name="zoneId">Stage identifier such as <c>S200</c>.</param>
    /// <returns>The zone with its floor, trigger and occupant maps.</returns>
    FieldZone GetOrLoadZone(string zoneId);

    /// <summary>Makes <paramref name="zone"/> the applied scene and presents it at the current player position.</summary>
    /// <param name="zone">Zone returned by <see cref="GetOrLoadZone"/>.</param>
    void ApplyScene(FieldZone zone);

    /// <summary>Runs one 60 Hz simulation step exactly as interactive play does.</summary>
    /// <param name="onMovementRejection">Receives the locomotion result when the floor or an occupant refused the step.</param>
    void SimulateStep(Action<MovementResult> onMovementRejection);

    /// <summary>Runs one simulation step that must not be refused by the floor or an occupant.</summary>
    /// <exception cref="InvalidOperationException">Locomotion rejected the held direction.</exception>
    void TickTestSimulation();

    /// <summary>Holds <paramref name="direction"/> for one simulation step.</summary>
    /// <param name="direction">Pad direction to hold.</param>
    /// <returns><see langword="true"/> when the player position changed.</returns>
    bool TryTestMove(SemanticDirection direction);

    /// <summary>Releases every direction key, then presses the keys of <paramref name="direction"/>.</summary>
    /// <param name="direction">Direction to hold, or <see cref="SemanticDirection.None"/> to release all.</param>
    void SetTestHeldDirection(SemanticDirection direction);

    /// <summary>Presses and releases the physical F3 key, toggling the collision overlay.</summary>
    void ToggleF3();

    /// <summary>Delivers a window focus notification as the window manager would.</summary>
    /// <param name="focused"><see langword="true"/> for focus in, <see langword="false"/> for focus out.</param>
    void SimulateWindowFocus(bool focused);

    /// <summary>Resolves the PNG of a decoded player sprite frame.</summary>
    /// <param name="frame">Player frame index from the animation table.</param>
    /// <returns>Absolute path of the frame image in the data pack.</returns>
    string ResolvePlayerFrame(int frame);

    /// <summary>Tells whether the player collision circle fits on open floor at <paramref name="position"/>.</summary>
    /// <param name="position">World position in map units.</param>
    /// <returns><see langword="true"/> when no blocked cell lies under the circle.</returns>
    bool CircleFreeAt(NumericsVector2 position);

    /// <summary>Builds the warp callbacks the host simulation uses, with a caller-chosen warning sink.</summary>
    /// <param name="warn">Receives each warning key and message.</param>
    /// <returns>Callbacks wired to the host scene change, climb and drop entry points.</returns>
    WarpCallbacks CreateWarpCallbacks(Action<string, string> warn);

    /// <summary>Returns an awaiter that completes after the next frame has been drawn.</summary>
    /// <returns>The <c>FramePostDraw</c> signal awaiter of the rendering server.</returns>
    SignalAwaiter NextFrameDrawn();

    /// <summary>Copies the last drawn viewport frame.</summary>
    /// <returns>A 1280x720 image of the viewport.</returns>
    Image CaptureViewport();

    /// <summary>Presents the current state and writes the drawn frame to a PNG.</summary>
    /// <param name="captureRoot">Directory that receives the PNG.</param>
    /// <param name="fileName">PNG file name.</param>
    /// <returns>A task that completes once the PNG is written.</returns>
    Task CaptureStill(string captureRoot, string fileName);

    /// <summary>Returns the probe capture directory, created if missing.</summary>
    /// <returns><c>DW3_CAPTURE_DIR</c>, or <c>captures</c> under the reference or data directory.</returns>
    string PrepareCaptureDirectory();

    /// <summary>Returns the probe video directory, created if missing.</summary>
    /// <returns><c>DW3_VIDEO_DIR</c>, or <c>video</c> under the reference or data directory.</returns>
    string PrepareVideoDirectory();

    /// <summary>Stops the engine with the probe verdict.</summary>
    /// <param name="exitCode">0 for a pass, 3 for a probe failure.</param>
    void Quit(int exitCode);

    /// <summary>
    /// Schedules a stop of the engine with the probe verdict, deferred to the next main-loop
    /// frame.  Use this instead of <see cref="Quit"/> when calling from an async continuation
    /// so that the main loop can settle before the engine tears down its native objects.
    /// </summary>
    /// <param name="exitCode">0 for a pass, 3 for a probe failure.</param>
    void QuitDeferred(int exitCode);
}
