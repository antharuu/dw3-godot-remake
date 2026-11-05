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
    /// Gets the user data-pack directory override. Default: platform XDG / AppData path
    /// (see <c>Dw3.Data.UserPaths.GetDataDirectory</c>).
    /// Environment variable: <c>DW3_DATA_DIR</c>.
    /// </summary>
    public string? DataDir { get; init; }

    /// <summary>
    /// Gets the disc-extractor binary path override, for development use. Default: binary
    /// resolved relative to the project root.
    /// Environment variable: <c>DW3_EXTRACTOR</c>.
    /// </summary>
    public string? ExtractorPath { get; init; }

    /// <summary>
    /// Gets the path of a disc image used in automated testing, bypassing the file-selection
    /// dialog at first launch. Default: null (dialog shown).
    /// Environment variable: <c>DW3_TEST_DISC</c>.
    /// </summary>
    public string? TestDisc { get; init; }

    /// <summary>
    /// Gets the directory where diagnostic probes write their screenshot captures.
    /// Default: null (falls back to the data-pack captures subdirectory).
    /// Environment variable: <c>DW3_CAPTURE_DIR</c>.
    /// </summary>
    public string? CaptureDir { get; init; }

    /// <summary>
    /// Gets the root directory of the generated reference images that probes compare against.
    /// Default: null (falls back to the data-pack directory).
    /// Environment variable: <c>DW3_REFERENCE_DIR</c>.
    /// </summary>
    public string? ReferenceDir { get; init; }

    /// <summary>
    /// Gets the root directory of encoded video output. Default: null (falls back to the
    /// reference directory / video subdirectory).
    /// Environment variable: <c>DW3_VIDEO_DIR</c>.
    /// </summary>
    public string? VideoDir { get; init; }

    /// <summary>
    /// Gets the root directory of the PSX reference frames used by the battle visual comparison
    /// probes. Default: null (probes that require it throw when unset).
    /// Environment variable: <c>DW3_PSX_REFERENCE_DIR</c>.
    /// </summary>
    public string? PsxReferenceDir { get; init; }

    /// <summary>
    /// Gets the zone the interactive session starts in (e.g. <c>S330</c>, <c>330</c>,
    /// <c>WSTAG330</c>). Default: null (normal S200 spawn). Non-imported zones are
    /// refused with a <c>CACHE_RESOURCE_UNAVAILABLE</c> error.
    /// Environment variable: <c>DW3_START_ZONE</c>.
    /// </summary>
    public string? StartZone { get; init; }

    /// <summary>
    /// Gets the spawn position override within the diagnostic zone, as <c>x,y</c> in map pixels.
    /// Default: null (zone-default or map centre).
    /// Environment variable: <c>DW3_START_POS</c>.
    /// </summary>
    public string? StartPos { get; init; }

    /// <summary>
    /// Gets the initial facing direction override (0–7, disc numbering) within the diagnostic zone.
    /// Default: null (source-backed default facing 1).
    /// Environment variable: <c>DW3_START_FACING</c>.
    /// </summary>
    public string? StartFacing { get; init; }

    /// <summary>
    /// Gets a value indicating whether the F3 collision / warp diagnostic overlay is on from startup.
    /// Default: false.
    /// Environment variable: <c>DW3_START_F3=1</c>.
    /// </summary>
    public bool StartF3 { get; init; }

    /// <summary>
    /// Gets the integer seed of the encounter and battle random-number generators, enabling
    /// reproducible sessions. Default: null (system random seed).
    /// Environment variable: <c>DW3_RNG_SEED</c>.
    /// </summary>
    public string? RngSeed { get; init; }

    /// <summary>
    /// Gets a value indicating whether the game captures a single frame once the spawn scene is reached, then quits immediately.
    /// Default: false.
    /// Environment variable: <c>DW3_CAPTURE_AND_QUIT=1</c>.
    /// </summary>
    public bool CaptureAndQuit { get; init; }

    /// <summary>Gets a value indicating whether the spawn capture should run after a normal session starts.
    /// True when either <see cref="CaptureAndQuit"/> is set or a <see cref="CaptureDir"/> override is active.
    /// No capture is written during ordinary player sessions.</summary>
    public bool SpawnCaptureEnabled => CaptureAndQuit || CaptureDir is not null;

    // ── Battle probe path overrides ───────────────────────────────────────────

    /// <summary>
    /// Gets the player battle-model path override used by the battle-model probe.
    /// Default: null (resolved from the data pack).
    /// Environment variable: <c>DW3_BATTLE_MODEL</c>.
    /// </summary>
    public string? BattleModel { get; init; }

    /// <summary>
    /// Gets the enemy battle-model path override used by the battle-model probe.
    /// Default: null (resolved from the data pack).
    /// Environment variable: <c>DW3_BATTLE_ENEMY_MODEL</c>.
    /// </summary>
    public string? BattleEnemyModel { get; init; }

    /// <summary>
    /// Gets the battle-stage path override used by the battle-model probe.
    /// Default: null (resolved from the data pack).
    /// Environment variable: <c>DW3_BATTLE_STAGE</c>.
    /// </summary>
    public string? BattleStage { get; init; }

    // ── Debug / probe flags ───────────────────────────────────────────────────
    // Set to "1" to activate. Any other value (including "true", "0", whitespace)
    // leaves the flag false. Only one probe runs per session; if multiple are set,
    // the first matched probe in the diagnostic runners' dispatch order wins.

    /// <summary>Gets a value indicating whether the first-launch probe runs: extraction flow, capture of S200, then quit.</summary>
    /// <remarks>Environment variable: <c>DW3_FIRST_LAUNCH_PROBE=1</c>.</remarks>
    public bool FirstLaunchProbe { get; init; }

    /// <summary>Gets a value indicating whether the input probe checks the input adapter's key and joypad mapping, then quits.</summary>
    /// <remarks>Environment variable: <c>DW3_INPUT_PROBE=1</c>.</remarks>
    public bool InputProbe { get; init; }

    /// <summary>Gets a value indicating whether the locomotion probe checks the movement simulation, then quits.</summary>
    /// <remarks>Environment variable: <c>DW3_LOCOMOTION_PROBE=1</c>.</remarks>
    public bool LocomotionProbe { get; init; }

    /// <summary>Gets a value indicating whether the warp probe checks every disc-backed warp transition of the loaded zone set, then quits.</summary>
    /// <remarks>Environment variable: <c>DW3_WARP_PROBE=1</c>.</remarks>
    public bool WarpProbe { get; init; }

    /// <summary>
    /// Gets a value indicating whether <see cref="WarpProbe"/> also exercises the missing-target error path.
    /// </summary>
    /// <remarks>Environment variable: <c>DW3_WARP_PROBE_MISSING_TARGET=1</c>.</remarks>
    public bool WarpProbeMissingTarget { get; init; }

    /// <summary>Gets a value indicating whether the F3 probe checks the collision and warp-trigger overlay, then quits.</summary>
    /// <remarks>Environment variable: <c>DW3_F3_PROBE=1</c>.</remarks>
    public bool F3Probe { get; init; }

    /// <summary>Gets a value indicating whether the F3 performance probe measures overlay frame time and FPS, then quits.</summary>
    /// <remarks>Environment variable: <c>DW3_F3_PERF_PROBE=1</c>.</remarks>
    public bool F3PerfProbe { get; init; }

    /// <summary>Gets a value indicating whether the depth probe checks background / foreground sprite ordering, then quits.</summary>
    /// <remarks>Environment variable: <c>DW3_DEPTH_PROBE=1</c>.</remarks>
    public bool DepthProbe { get; init; }

    /// <summary>Gets a value indicating whether the water probe checks static and animated water rendering in S200, then quits.</summary>
    /// <remarks>Environment variable: <c>DW3_WATER_PROBE=1</c>.</remarks>
    public bool WaterProbe { get; init; }

    /// <summary>Gets a value indicating whether the sort-Y probe checks depth ordering between the player and sorted water objects, then quits.</summary>
    /// <remarks>Environment variable: <c>DW3_SORTY_PROBE=1</c>.</remarks>
    public bool SortYProbe { get; init; }

    /// <summary>Gets a value indicating whether the cue probe checks the exit-cue animation and its presence in the framebuffer, then quits.</summary>
    /// <remarks>Environment variable: <c>DW3_CUE_PROBE=1</c>.</remarks>
    public bool CueProbe { get; init; }

    /// <summary>Gets a value indicating whether the canal water probe checks the canal water rendering, then quits.</summary>
    /// <remarks>Environment variable: <c>DW3_CANAL_WATER_PROBE=1</c>.</remarks>
    public bool CanalWaterProbe { get; init; }

    /// <summary>Gets a value indicating whether the first-milestone acceptance route runs (S200 ↔ S235 loop with three warp commits), then quits.</summary>
    /// <remarks>Environment variable: <c>DW3_M1_ACCEPTANCE_TEST=1</c>.</remarks>
    public bool M1AcceptanceTest { get; init; }

    /// <summary>Gets a value indicating whether the navigation probe checks multi-zone travel (S200, S210, S235), then quits.</summary>
    /// <remarks>Environment variable: <c>DW3_M2_NAV_PROBE=1</c>.</remarks>
    public bool M2NavProbe { get; init; }

    /// <summary>Gets a value indicating whether the video probe records the multi-zone traversal as a 60 fps video, then quits.</summary>
    /// <remarks>Environment variable: <c>DW3_M2_VIDEO_PROBE=1</c>.</remarks>
    public bool M2VideoProbe { get; init; }

    /// <summary>
    /// Gets a value indicating whether the conditional probe checks that story-gated exits pass as unevaluated (condition not enforced), then quits.
    /// </summary>
    /// <remarks>Environment variable: <c>DW3_CONDITIONAL_PROBE=1</c>.</remarks>
    public bool ConditionalProbe { get; init; }

    /// <summary>Gets a value indicating whether the gated-bridge probe checks the S200 ↔ S205 crossing, then quits.</summary>
    /// <remarks>Environment variable: <c>DW3_GATED_BRIDGE_PROBE=1</c>.</remarks>
    public bool GatedBridgeProbe { get; init; }

    /// <summary>Gets a value indicating whether the NPC probe checks NPC pixels and sort-Y occlusion in S200/S210/S235, then quits.</summary>
    /// <remarks>Environment variable: <c>DW3_NPC_PROBE=1</c>.</remarks>
    public bool NpcProbe { get; init; }

    /// <summary>Gets a value indicating whether the object probe checks interactive-object rendering, then quits.</summary>
    /// <remarks>Environment variable: <c>DW3_OBJECT_PROBE=1</c>.</remarks>
    public bool ObjectProbe { get; init; }

    /// <summary>Gets a value indicating whether the climb probe checks the wall climb (ascent, descent, stuck recovery), then quits.</summary>
    /// <remarks>Environment variable: <c>DW3_CLIMB_PROBE=1</c>.</remarks>
    public bool ClimbProbe { get; init; }

    /// <summary>Gets a value indicating whether the drop probe checks the cliff drop, then quits.</summary>
    /// <remarks>Environment variable: <c>DW3_DROP_PROBE=1</c>.</remarks>
    public bool DropProbe { get; init; }

    /// <summary>Gets a value indicating whether the dialogue probe checks the glyph atlas, the 9-patch bubble and advance/rearm, then quits.</summary>
    /// <remarks>Environment variable: <c>DW3_DIALOGUE_PROBE=1</c>.</remarks>
    public bool DialogueProbe { get; init; }

    /// <summary>Gets a value indicating whether the icon probe checks the connection-indicator sprite and its smooth follow, then quits.</summary>
    /// <remarks>Environment variable: <c>DW3_ICON_PROBE=1</c>.</remarks>
    public bool IconProbe { get; init; }

    /// <summary>Gets a value indicating whether the battle camera probe checks camera position and interpolation, then quits.</summary>
    /// <remarks>Environment variable: <c>DW3_BATTLE_CAMERA_PROBE=1</c>.</remarks>
    public bool BattleCameraProbe { get; init; }

    /// <summary>Gets a value indicating whether the battle HUD probe checks the sprite sheet, cursor and offsets, then quits.</summary>
    /// <remarks>Environment variable: <c>DW3_BATTLE_HUD_PROBE=1</c>.</remarks>
    public bool BattleHudProbe { get; init; }

    /// <summary>Gets a value indicating whether the battle input probe checks menu navigation and press edges, then quits.</summary>
    /// <remarks>Environment variable: <c>DW3_BATTLE_INPUT_PROBE=1</c>.</remarks>
    public bool BattleInputProbe { get; init; }

    /// <summary>Gets a value indicating whether the battle play probe runs a full cycle (select, resolve, rewards), then quits.</summary>
    /// <remarks>Environment variable: <c>DW3_BATTLE_PLAY_PROBE=1</c>.</remarks>
    public bool BattlePlayProbe { get; init; }

    /// <summary>Gets a value indicating whether the battle motion probe checks motion decoding and playback, then quits.</summary>
    /// <remarks>Environment variable: <c>DW3_BATTLE_MOTION_PROBE=1</c>.</remarks>
    public bool BattleMotionProbe { get; init; }

    /// <summary>Gets a value indicating whether the battle action probe records real command boundaries and stage advancement, then quits.</summary>
    /// <remarks>Environment variable: <c>DW3_BATTLE_ACTION_PROBE=1</c>.</remarks>
    public bool BattleActionProbe { get; init; }

    /// <summary>Gets a value indicating whether the battle font probe checks HUD glyph rasterisation, then quits.</summary>
    /// <remarks>Environment variable: <c>DW3_BATTLE_FONT_PROBE=1</c>.</remarks>
    public bool BattleFontProbe { get; init; }

    /// <summary>Gets a value indicating whether the battle face probe checks face-sprite decoding and drawing, then quits.</summary>
    /// <remarks>Environment variable: <c>DW3_BATTLE_FACE_PROBE=1</c>.</remarks>
    public bool BattleFaceProbe { get; init; }

    /// <summary>Gets a value indicating whether the battle model probe checks 3D model decoding (bones, meshes, texture, motion), then quits.</summary>
    /// <remarks>Environment variable: <c>DW3_BATTLE_MODEL_PROBE=1</c>.</remarks>
    public bool BattleModelProbe { get; init; }

    /// <summary>Gets a value indicating whether the battle entrance probe checks the entrance fade-in, then quits.</summary>
    /// <remarks>Environment variable: <c>DW3_BATTLE_ENTRANCE_PROBE=1</c>.</remarks>
    public bool BattleEntranceProbe { get; init; }

    /// <summary>Gets a value indicating whether the Kotemon lighting probe checks that model's texture and lighting, then quits.</summary>
    /// <remarks>Environment variable: <c>DW3_BATTLE_KOTEMON_LIGHT_PROBE=1</c>.</remarks>
    public bool BattleKotemonLightProbe { get; init; }

    /// <summary>Gets a value indicating whether the battle light probe checks ambient and directional lighting, then quits.</summary>
    /// <remarks>Environment variable: <c>DW3_BATTLE_LIGHT_PROBE=1</c>.</remarks>
    public bool BattleLightProbe { get; init; }

    /// <summary>Gets a value indicating whether the battle report probe checks the end-of-fight report (rewards, growth display), then quits.</summary>
    /// <remarks>Environment variable: <c>DW3_BATTLE_REPORT_PROBE=1</c>.</remarks>
    public bool BattleReportProbe { get; init; }

    /// <summary>Gets a value indicating whether the fighter sheet is rendered for every loaded fighter, then the game quits.</summary>
    /// <remarks>Environment variable: <c>DW3_FIGHTER_SHEET=1</c>.</remarks>
    public bool FighterSheet { get; init; }

    /// <summary>
    /// Gets a value indicating whether the SQLite probe checks the native library by writing and reading back one row, then quits.
    /// </summary>
    /// <remarks>Environment variable: <c>DW3_SQLITE_PROBE=1</c>.</remarks>
    public bool SqliteProbe { get; init; }

    // ── Factory methods ───────────────────────────────────────────────────────

    /// <summary>Creates options from the current process environment variables.</summary>
    /// <returns>Options with every variable read once. Later changes to the environment are not seen.</returns>
    public static HostStartupOptions FromEnvironment() =>
        From(System.Environment.GetEnvironmentVariable);

    /// <summary>
    /// Creates options from a caller-supplied variable reader. Intended for unit tests
    /// that need to supply a controlled variable map without touching the real process
    /// environment.
    /// </summary>
    /// <param name="getEnv">Function that returns the value for a named variable, or null
    /// when the variable is absent.</param>
    /// <returns>Options where each flag is true only for the exact value <c>1</c> and each path or text value is passed through unchanged, null when absent.</returns>
    public static HostStartupOptions From(Func<string, string?> getEnv) => new()
    {
        DataDir = getEnv("DW3_DATA_DIR"),
        ExtractorPath = getEnv("DW3_EXTRACTOR"),
        TestDisc = getEnv("DW3_TEST_DISC"),
        CaptureDir = getEnv("DW3_CAPTURE_DIR"),
        ReferenceDir = getEnv("DW3_REFERENCE_DIR"),
        VideoDir = getEnv("DW3_VIDEO_DIR"),
        PsxReferenceDir = getEnv("DW3_PSX_REFERENCE_DIR"),
        StartZone = getEnv("DW3_START_ZONE"),
        StartPos = getEnv("DW3_START_POS"),
        StartFacing = getEnv("DW3_START_FACING"),
        StartF3 = getEnv("DW3_START_F3") == "1",
        RngSeed = getEnv("DW3_RNG_SEED"),
        CaptureAndQuit = getEnv("DW3_CAPTURE_AND_QUIT") == "1",
        BattleModel = getEnv("DW3_BATTLE_MODEL"),
        BattleEnemyModel = getEnv("DW3_BATTLE_ENEMY_MODEL"),
        BattleStage = getEnv("DW3_BATTLE_STAGE"),

        FirstLaunchProbe = getEnv("DW3_FIRST_LAUNCH_PROBE") == "1",
        InputProbe = getEnv("DW3_INPUT_PROBE") == "1",
        LocomotionProbe = getEnv("DW3_LOCOMOTION_PROBE") == "1",
        WarpProbe = getEnv("DW3_WARP_PROBE") == "1",
        WarpProbeMissingTarget = getEnv("DW3_WARP_PROBE_MISSING_TARGET") == "1",
        F3Probe = getEnv("DW3_F3_PROBE") == "1",
        F3PerfProbe = getEnv("DW3_F3_PERF_PROBE") == "1",
        DepthProbe = getEnv("DW3_DEPTH_PROBE") == "1",
        WaterProbe = getEnv("DW3_WATER_PROBE") == "1",
        SortYProbe = getEnv("DW3_SORTY_PROBE") == "1",
        CueProbe = getEnv("DW3_CUE_PROBE") == "1",
        CanalWaterProbe = getEnv("DW3_CANAL_WATER_PROBE") == "1",
        M1AcceptanceTest = getEnv("DW3_M1_ACCEPTANCE_TEST") == "1",
        M2NavProbe = getEnv("DW3_M2_NAV_PROBE") == "1",
        M2VideoProbe = getEnv("DW3_M2_VIDEO_PROBE") == "1",
        ConditionalProbe = getEnv("DW3_CONDITIONAL_PROBE") == "1",
        GatedBridgeProbe = getEnv("DW3_GATED_BRIDGE_PROBE") == "1",
        NpcProbe = getEnv("DW3_NPC_PROBE") == "1",
        ObjectProbe = getEnv("DW3_OBJECT_PROBE") == "1",
        ClimbProbe = getEnv("DW3_CLIMB_PROBE") == "1",
        DropProbe = getEnv("DW3_DROP_PROBE") == "1",
        DialogueProbe = getEnv("DW3_DIALOGUE_PROBE") == "1",
        IconProbe = getEnv("DW3_ICON_PROBE") == "1",
        BattleCameraProbe = getEnv("DW3_BATTLE_CAMERA_PROBE") == "1",
        BattleHudProbe = getEnv("DW3_BATTLE_HUD_PROBE") == "1",
        BattleInputProbe = getEnv("DW3_BATTLE_INPUT_PROBE") == "1",
        BattlePlayProbe = getEnv("DW3_BATTLE_PLAY_PROBE") == "1",
        BattleMotionProbe = getEnv("DW3_BATTLE_MOTION_PROBE") == "1",
        BattleActionProbe = getEnv("DW3_BATTLE_ACTION_PROBE") == "1",
        BattleFontProbe = getEnv("DW3_BATTLE_FONT_PROBE") == "1",
        BattleFaceProbe = getEnv("DW3_BATTLE_FACE_PROBE") == "1",
        BattleModelProbe = getEnv("DW3_BATTLE_MODEL_PROBE") == "1",
        BattleEntranceProbe = getEnv("DW3_BATTLE_ENTRANCE_PROBE") == "1",
        BattleKotemonLightProbe = getEnv("DW3_BATTLE_KOTEMON_LIGHT_PROBE") == "1",
        BattleLightProbe = getEnv("DW3_BATTLE_LIGHT_PROBE") == "1",
        BattleReportProbe = getEnv("DW3_BATTLE_REPORT_PROBE") == "1",
        FighterSheet = getEnv("DW3_FIGHTER_SHEET") == "1",
        SqliteProbe = getEnv("DW3_SQLITE_PROBE") == "1",
    };
}
