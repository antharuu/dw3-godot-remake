namespace Dw3Production;

/// <summary>
/// Captures all <c>DW3_*</c> environment variables once at application startup.
/// <see cref="GameHost._Ready"/> reads properties from this class instead of calling
/// <c>Environment.GetEnvironmentVariable</c> inline, so the environment is parsed in one
/// place with documented defaults.
/// </summary>
/// <remarks>
/// Variables are divided into two groups — user configuration and debug / probe flags —
/// documented fully in <c>docs/DEVELOPMENT.md</c>.
///
/// Use <see cref="FromEnvironment"/> for production startup and <see cref="From"/> for
/// unit tests that supply a controlled variable map without touching the real environment.
/// </remarks>
public sealed class HostStartupOptions
{
    // ── User configuration ────────────────────────────────────────────────────

    /// <summary>
    /// Override the user data-pack directory. Default: platform XDG / AppData path
    /// (see <c>Dw3.Data.UserPaths.GetDataDirectory</c>).
    /// Environment variable: <c>DW3_DATA_DIR</c>.
    /// </summary>
    public string? DataDir { get; init; }

    /// <summary>
    /// Override the disc-extractor binary path for development use. Default: binary
    /// resolved relative to the project root.
    /// Environment variable: <c>DW3_EXTRACTOR</c>.
    /// </summary>
    public string? ExtractorPath { get; init; }

    /// <summary>
    /// Path to a disc image used in automated testing, bypassing the file-selection
    /// dialog at first launch. Default: null (dialog shown).
    /// Environment variable: <c>DW3_TEST_DISC</c>.
    /// </summary>
    public string? TestDisc { get; init; }

    /// <summary>
    /// Directory for screenshot captures produced by diagnostic probes.
    /// Default: null (falls back to the data-pack captures subdirectory).
    /// Environment variable: <c>DW3_CAPTURE_DIR</c>.
    /// </summary>
    public string? CaptureDir { get; init; }

    /// <summary>
    /// Root directory for generated reference images used in probe comparisons.
    /// Default: null (falls back to the data-pack directory).
    /// Environment variable: <c>DW3_REFERENCE_DIR</c>.
    /// </summary>
    public string? ReferenceDir { get; init; }

    /// <summary>
    /// Root directory for encoded video output. Default: null (falls back to the
    /// reference directory / video subdirectory).
    /// Environment variable: <c>DW3_VIDEO_DIR</c>.
    /// </summary>
    public string? VideoDir { get; init; }

    /// <summary>
    /// Root directory for PSX reference frames used in battle visual comparison
    /// probes. Default: null (probes that require it throw when unset).
    /// Environment variable: <c>DW3_PSX_REFERENCE_DIR</c>.
    /// </summary>
    public string? PsxReferenceDir { get; init; }

    /// <summary>
    /// Start the interactive session in a specific zone (e.g. <c>S330</c>, <c>330</c>,
    /// <c>WSTAG330</c>). Default: null (normal S200 spawn). Non-imported zones are
    /// refused with a <c>CACHE_RESOURCE_UNAVAILABLE</c> error.
    /// Environment variable: <c>DW3_START_ZONE</c>.
    /// </summary>
    public string? StartZone { get; init; }

    /// <summary>
    /// Override the spawn position within the diagnostic zone, as <c>x,y</c>.
    /// Default: null (zone-default or map centre).
    /// Environment variable: <c>DW3_START_POS</c>.
    /// </summary>
    public string? StartPos { get; init; }

    /// <summary>
    /// Override the initial facing direction (0–7) within the diagnostic zone.
    /// Default: null (source-backed default facing 1).
    /// Environment variable: <c>DW3_START_FACING</c>.
    /// </summary>
    public string? StartFacing { get; init; }

    /// <summary>
    /// Enable the F3 collision / warp diagnostic overlay at startup.
    /// Default: false.
    /// Environment variable: <c>DW3_START_F3=1</c>.
    /// </summary>
    public bool StartF3 { get; init; }

    /// <summary>
    /// Integer seed for the encounter and battle random-number generators, enabling
    /// reproducible sessions. Default: null (system random seed).
    /// Environment variable: <c>DW3_RNG_SEED</c>.
    /// </summary>
    public string? RngSeed { get; init; }

    /// <summary>
    /// Capture a single frame after reaching the spawn scene, then quit immediately.
    /// Default: false.
    /// Environment variable: <c>DW3_CAPTURE_AND_QUIT=1</c>.
    /// </summary>
    public bool CaptureAndQuit { get; init; }

    // ── Battle probe path overrides ───────────────────────────────────────────

    /// <summary>
    /// Override the player battle-model path used by the battle-model probe.
    /// Default: null (resolved from the data pack).
    /// Environment variable: <c>DW3_BATTLE_MODEL</c>.
    /// </summary>
    public string? BattleModel { get; init; }

    /// <summary>
    /// Override the enemy battle-model path used by the battle-model probe.
    /// Default: null (resolved from the data pack).
    /// Environment variable: <c>DW3_BATTLE_ENEMY_MODEL</c>.
    /// </summary>
    public string? BattleEnemyModel { get; init; }

    /// <summary>
    /// Override the battle-stage path used by the battle-model probe.
    /// Default: null (resolved from the data pack).
    /// Environment variable: <c>DW3_BATTLE_STAGE</c>.
    /// </summary>
    public string? BattleStage { get; init; }

    // ── Debug / probe flags ───────────────────────────────────────────────────
    // Set to "1" to activate. Any other value (including "true", "0", whitespace)
    // leaves the flag false. Only one probe runs per session; if multiple are set,
    // the first matched probe in the diagnostic runners' dispatch order wins.

    /// <summary>Run the first-launch extraction flow, capture S200, then quit.</summary>
    /// <remarks>Environment variable: <c>DW3_FIRST_LAUNCH_PROBE=1</c>.</remarks>
    public bool FirstLaunchProbe { get; init; }

    /// <summary>Verify the input adapter's key and joypad mapping, then quit.</summary>
    /// <remarks>Environment variable: <c>DW3_INPUT_PROBE=1</c>.</remarks>
    public bool InputProbe { get; init; }

    /// <summary>Run the locomotion-simulation verification probe, then quit.</summary>
    /// <remarks>Environment variable: <c>DW3_LOCOMOTION_PROBE=1</c>.</remarks>
    public bool LocomotionProbe { get; init; }

    /// <summary>Verify all source-backed warp transitions for the loaded zone set, then quit.</summary>
    /// <remarks>Environment variable: <c>DW3_WARP_PROBE=1</c>.</remarks>
    public bool WarpProbe { get; init; }

    /// <summary>
    /// Modifier for <see cref="WarpProbe"/>: also exercise the missing-target error path.
    /// </summary>
    /// <remarks>Environment variable: <c>DW3_WARP_PROBE_MISSING_TARGET=1</c>.</remarks>
    public bool WarpProbeMissingTarget { get; init; }

    /// <summary>Verify the F3 collision and warp-trigger overlay, then quit.</summary>
    /// <remarks>Environment variable: <c>DW3_F3_PROBE=1</c>.</remarks>
    public bool F3Probe { get; init; }

    /// <summary>Measure F3 rendering performance (frame time and FPS), then quit.</summary>
    /// <remarks>Environment variable: <c>DW3_F3_PERF_PROBE=1</c>.</remarks>
    public bool F3PerfProbe { get; init; }

    /// <summary>Verify background / foreground sprite depth ordering, then quit.</summary>
    /// <remarks>Environment variable: <c>DW3_DEPTH_PROBE=1</c>.</remarks>
    public bool DepthProbe { get; init; }

    /// <summary>Verify static and animated water-effect rendering in S200, then quit.</summary>
    /// <remarks>Environment variable: <c>DW3_WATER_PROBE=1</c>.</remarks>
    public bool WaterProbe { get; init; }

    /// <summary>Verify sort-Y depth ordering between the player and sorted water objects, then quit.</summary>
    /// <remarks>Environment variable: <c>DW3_SORTY_PROBE=1</c>.</remarks>
    public bool SortYProbe { get; init; }

    /// <summary>Verify exit-cue animation and framebuffer presence, then quit.</summary>
    /// <remarks>Environment variable: <c>DW3_CUE_PROBE=1</c>.</remarks>
    public bool CueProbe { get; init; }

    /// <summary>Verify the canal water-effect rendering, then quit.</summary>
    /// <remarks>Environment variable: <c>DW3_CANAL_WATER_PROBE=1</c>.</remarks>
    public bool CanalWaterProbe { get; init; }

    /// <summary>Run the M1 acceptance route (S200 ↔ S235 three-commit loop), then quit.</summary>
    /// <remarks>Environment variable: <c>DW3_M1_ACCEPTANCE_TEST=1</c>.</remarks>
    public bool M1AcceptanceTest { get; init; }

    /// <summary>Verify multi-zone navigation (S200, S210, S235), then quit.</summary>
    /// <remarks>Environment variable: <c>DW3_M2_NAV_PROBE=1</c>.</remarks>
    public bool M2NavProbe { get; init; }

    /// <summary>Capture the M2 multi-zone traversal as a 60 fps video, then quit.</summary>
    /// <remarks>Environment variable: <c>DW3_M2_VIDEO_PROBE=1</c>.</remarks>
    public bool M2VideoProbe { get; init; }

    /// <summary>
    /// Verify story-gated exits pass through as unevaluated (condition not enforced), then quit.
    /// </summary>
    /// <remarks>Environment variable: <c>DW3_CONDITIONAL_PROBE=1</c>.</remarks>
    public bool ConditionalProbe { get; init; }

    /// <summary>Verify the gated-bridge traversal (S200 ↔ S205), then quit.</summary>
    /// <remarks>Environment variable: <c>DW3_GATED_BRIDGE_PROBE=1</c>.</remarks>
    public bool GatedBridgeProbe { get; init; }

    /// <summary>Verify NPC pixel rendering and sort-Y occlusion in S200/S210/S235, then quit.</summary>
    /// <remarks>Environment variable: <c>DW3_NPC_PROBE=1</c>.</remarks>
    public bool NpcProbe { get; init; }

    /// <summary>Verify interactive-object rendering, then quit.</summary>
    /// <remarks>Environment variable: <c>DW3_OBJECT_PROBE=1</c>.</remarks>
    public bool ObjectProbe { get; init; }

    /// <summary>Verify the ledge-climb controller (ascent / descent / stuck recovery), then quit.</summary>
    /// <remarks>Environment variable: <c>DW3_CLIMB_PROBE=1</c>.</remarks>
    public bool ClimbProbe { get; init; }

    /// <summary>Verify the cliff-drop controller, then quit.</summary>
    /// <remarks>Environment variable: <c>DW3_DROP_PROBE=1</c>.</remarks>
    public bool DropProbe { get; init; }

    /// <summary>Verify dialogue rendering (glyph atlas, 9-patch bubble, advance/rearm), then quit.</summary>
    /// <remarks>Environment variable: <c>DW3_DIALOGUE_PROBE=1</c>.</remarks>
    public bool DialogueProbe { get; init; }

    /// <summary>Verify the player connection-icon sprite and smooth-follow tracking, then quit.</summary>
    /// <remarks>Environment variable: <c>DW3_ICON_PROBE=1</c>.</remarks>
    public bool IconProbe { get; init; }

    /// <summary>Verify the battle camera position and interpolation, then quit.</summary>
    /// <remarks>Environment variable: <c>DW3_BATTLE_CAMERA_PROBE=1</c>.</remarks>
    public bool BattleCameraProbe { get; init; }

    /// <summary>Verify battle HUD layout (sprite sheet, cursor, offsets), then quit.</summary>
    /// <remarks>Environment variable: <c>DW3_BATTLE_HUD_PROBE=1</c>.</remarks>
    public bool BattleHudProbe { get; init; }

    /// <summary>Verify battle input handling (menu navigation, edge detection), then quit.</summary>
    /// <remarks>Environment variable: <c>DW3_BATTLE_INPUT_PROBE=1</c>.</remarks>
    public bool BattleInputProbe { get; init; }

    /// <summary>Verify a complete battle play-flow cycle (select, resolve, rewards), then quit.</summary>
    /// <remarks>Environment variable: <c>DW3_BATTLE_PLAY_PROBE=1</c>.</remarks>
    public bool BattlePlayProbe { get; init; }

    /// <summary>Verify battle motion decoding and animation playback, then quit.</summary>
    /// <remarks>Environment variable: <c>DW3_BATTLE_MOTION_PROBE=1</c>.</remarks>
    public bool BattleMotionProbe { get; init; }

    /// <summary>Verify battle HUD font rendering (glyph atlas rasterisation), then quit.</summary>
    /// <remarks>Environment variable: <c>DW3_BATTLE_FONT_PROBE=1</c>.</remarks>
    public bool BattleFontProbe { get; init; }

    /// <summary>Verify battle face-sprite decoding and rendering, then quit.</summary>
    /// <remarks>Environment variable: <c>DW3_BATTLE_FACE_PROBE=1</c>.</remarks>
    public bool BattleFaceProbe { get; init; }

    /// <summary>Verify battle 3D model decoding (bones, meshes, texture, motion), then quit.</summary>
    /// <remarks>Environment variable: <c>DW3_BATTLE_MODEL_PROBE=1</c>.</remarks>
    public bool BattleModelProbe { get; init; }

    /// <summary>Verify the battle entrance fade-in animation, then quit.</summary>
    /// <remarks>Environment variable: <c>DW3_BATTLE_ENTRANCE_PROBE=1</c>.</remarks>
    public bool BattleEntranceProbe { get; init; }

    /// <summary>Verify Kotemon-specific texture and lighting, then quit.</summary>
    /// <remarks>Environment variable: <c>DW3_BATTLE_KOTEMON_LIGHT_PROBE=1</c>.</remarks>
    public bool BattleKotemonLightProbe { get; init; }

    /// <summary>Verify general battle lighting (ambient, directional), then quit.</summary>
    /// <remarks>Environment variable: <c>DW3_BATTLE_LIGHT_PROBE=1</c>.</remarks>
    public bool BattleLightProbe { get; init; }

    /// <summary>Verify the end-of-fight report screen (rewards, growth display), then quit.</summary>
    /// <remarks>Environment variable: <c>DW3_BATTLE_REPORT_PROBE=1</c>.</remarks>
    public bool BattleReportProbe { get; init; }

    /// <summary>Render a full fighter sprite sheet for all loaded fighters, then quit.</summary>
    /// <remarks>Environment variable: <c>DW3_FIGHTER_SHEET=1</c>.</remarks>
    public bool FighterSheet { get; init; }

    /// <summary>
    /// Verify the SQLite native library by writing and reading back one row, then quit.
    /// </summary>
    /// <remarks>Environment variable: <c>DW3_SQLITE_PROBE=1</c>.</remarks>
    public bool SqliteProbe { get; init; }

    // ── Factory methods ───────────────────────────────────────────────────────

    /// <summary>Creates options from the current process environment variables.</summary>
    public static HostStartupOptions FromEnvironment() =>
        From(System.Environment.GetEnvironmentVariable);

    /// <summary>
    /// Creates options from a caller-supplied variable reader. Intended for unit tests
    /// that need to supply a controlled variable map without touching the real process
    /// environment.
    /// </summary>
    /// <param name="getEnv">Function that returns the value for a named variable, or null
    /// when the variable is absent.</param>
    public static HostStartupOptions From(Func<string, string?> getEnv) => new()
    {
        DataDir          = getEnv("DW3_DATA_DIR"),
        ExtractorPath    = getEnv("DW3_EXTRACTOR"),
        TestDisc         = getEnv("DW3_TEST_DISC"),
        CaptureDir       = getEnv("DW3_CAPTURE_DIR"),
        ReferenceDir     = getEnv("DW3_REFERENCE_DIR"),
        VideoDir         = getEnv("DW3_VIDEO_DIR"),
        PsxReferenceDir  = getEnv("DW3_PSX_REFERENCE_DIR"),
        StartZone        = getEnv("DW3_START_ZONE"),
        StartPos         = getEnv("DW3_START_POS"),
        StartFacing      = getEnv("DW3_START_FACING"),
        StartF3          = getEnv("DW3_START_F3") == "1",
        RngSeed          = getEnv("DW3_RNG_SEED"),
        CaptureAndQuit   = getEnv("DW3_CAPTURE_AND_QUIT") == "1",
        BattleModel      = getEnv("DW3_BATTLE_MODEL"),
        BattleEnemyModel = getEnv("DW3_BATTLE_ENEMY_MODEL"),
        BattleStage      = getEnv("DW3_BATTLE_STAGE"),

        FirstLaunchProbe        = getEnv("DW3_FIRST_LAUNCH_PROBE") == "1",
        InputProbe              = getEnv("DW3_INPUT_PROBE") == "1",
        LocomotionProbe         = getEnv("DW3_LOCOMOTION_PROBE") == "1",
        WarpProbe               = getEnv("DW3_WARP_PROBE") == "1",
        WarpProbeMissingTarget  = getEnv("DW3_WARP_PROBE_MISSING_TARGET") == "1",
        F3Probe                 = getEnv("DW3_F3_PROBE") == "1",
        F3PerfProbe             = getEnv("DW3_F3_PERF_PROBE") == "1",
        DepthProbe              = getEnv("DW3_DEPTH_PROBE") == "1",
        WaterProbe              = getEnv("DW3_WATER_PROBE") == "1",
        SortYProbe              = getEnv("DW3_SORTY_PROBE") == "1",
        CueProbe                = getEnv("DW3_CUE_PROBE") == "1",
        CanalWaterProbe         = getEnv("DW3_CANAL_WATER_PROBE") == "1",
        M1AcceptanceTest        = getEnv("DW3_M1_ACCEPTANCE_TEST") == "1",
        M2NavProbe              = getEnv("DW3_M2_NAV_PROBE") == "1",
        M2VideoProbe            = getEnv("DW3_M2_VIDEO_PROBE") == "1",
        ConditionalProbe        = getEnv("DW3_CONDITIONAL_PROBE") == "1",
        GatedBridgeProbe        = getEnv("DW3_GATED_BRIDGE_PROBE") == "1",
        NpcProbe                = getEnv("DW3_NPC_PROBE") == "1",
        ObjectProbe             = getEnv("DW3_OBJECT_PROBE") == "1",
        ClimbProbe              = getEnv("DW3_CLIMB_PROBE") == "1",
        DropProbe               = getEnv("DW3_DROP_PROBE") == "1",
        DialogueProbe           = getEnv("DW3_DIALOGUE_PROBE") == "1",
        IconProbe               = getEnv("DW3_ICON_PROBE") == "1",
        BattleCameraProbe       = getEnv("DW3_BATTLE_CAMERA_PROBE") == "1",
        BattleHudProbe          = getEnv("DW3_BATTLE_HUD_PROBE") == "1",
        BattleInputProbe        = getEnv("DW3_BATTLE_INPUT_PROBE") == "1",
        BattlePlayProbe         = getEnv("DW3_BATTLE_PLAY_PROBE") == "1",
        BattleMotionProbe       = getEnv("DW3_BATTLE_MOTION_PROBE") == "1",
        BattleFontProbe         = getEnv("DW3_BATTLE_FONT_PROBE") == "1",
        BattleFaceProbe         = getEnv("DW3_BATTLE_FACE_PROBE") == "1",
        BattleModelProbe        = getEnv("DW3_BATTLE_MODEL_PROBE") == "1",
        BattleEntranceProbe     = getEnv("DW3_BATTLE_ENTRANCE_PROBE") == "1",
        BattleKotemonLightProbe = getEnv("DW3_BATTLE_KOTEMON_LIGHT_PROBE") == "1",
        BattleLightProbe        = getEnv("DW3_BATTLE_LIGHT_PROBE") == "1",
        BattleReportProbe       = getEnv("DW3_BATTLE_REPORT_PROBE") == "1",
        FighterSheet            = getEnv("DW3_FIGHTER_SHEET") == "1",
        SqliteProbe             = getEnv("DW3_SQLITE_PROBE") == "1",
    };
}
