using Dw3Production;
using Xunit;

namespace Dw3.Game.Tests;

/// <summary>
/// Verifies that HostStartupOptions correctly parses all DW3_* variables from an
/// injected environment reader without touching the real process environment.
/// </summary>
public sealed class HostStartupOptionsTests
{
    // ── Defaults ─────────────────────────────────────────────────────────────
    [Fact]
    public void AllProbeFlagsDefaultToFalse()
    {
        var opts = HostStartupOptions.From(_ => null);

        Assert.False(opts.FirstLaunchProbe);
        Assert.False(opts.InputProbe);
        Assert.False(opts.LocomotionProbe);
        Assert.False(opts.WarpProbe);
        Assert.False(opts.WarpProbeMissingTarget);
        Assert.False(opts.F3Probe);
        Assert.False(opts.F3PerfProbe);
        Assert.False(opts.DepthProbe);
        Assert.False(opts.WaterProbe);
        Assert.False(opts.SortYProbe);
        Assert.False(opts.CueProbe);
        Assert.False(opts.CanalWaterProbe);
        Assert.False(opts.M1AcceptanceTest);
        Assert.False(opts.M2NavProbe);
        Assert.False(opts.M2VideoProbe);
        Assert.False(opts.ConditionalProbe);
        Assert.False(opts.GatedBridgeProbe);
        Assert.False(opts.NpcProbe);
        Assert.False(opts.ObjectProbe);
        Assert.False(opts.ClimbProbe);
        Assert.False(opts.DropProbe);
        Assert.False(opts.DialogueProbe);
        Assert.False(opts.IconProbe);
        Assert.False(opts.BattleCameraProbe);
        Assert.False(opts.BattleHudProbe);
        Assert.False(opts.BattleInputProbe);
        Assert.False(opts.BattlePlayProbe);
        Assert.False(opts.BattleMotionProbe);
        Assert.False(opts.BattleFontProbe);
        Assert.False(opts.BattleFaceProbe);
        Assert.False(opts.BattleModelProbe);
        Assert.False(opts.BattleEntranceProbe);
        Assert.False(opts.BattleKotemonLightProbe);
        Assert.False(opts.BattleLightProbe);
        Assert.False(opts.BattleReportProbe);
        Assert.False(opts.FighterSheet);
        Assert.False(opts.SqliteProbe);
        Assert.False(opts.StartF3);
        Assert.False(opts.CaptureAndQuit);
    }

    [Fact]
    public void AllStringOverridesDefaultToNull()
    {
        var opts = HostStartupOptions.From(_ => null);

        Assert.Null(opts.DataDir);
        Assert.Null(opts.ExtractorPath);
        Assert.Null(opts.TestDisc);
        Assert.Null(opts.CaptureDir);
        Assert.Null(opts.ReferenceDir);
        Assert.Null(opts.VideoDir);
        Assert.Null(opts.PsxReferenceDir);
        Assert.Null(opts.StartZone);
        Assert.Null(opts.StartPos);
        Assert.Null(opts.StartFacing);
        Assert.Null(opts.RngSeed);
        Assert.Null(opts.BattleModel);
        Assert.Null(opts.BattleEnemyModel);
        Assert.Null(opts.BattleStage);
    }

    // ── Probe flag parsing ────────────────────────────────────────────────────
    [Fact]
    public void DialogueProbeParsedFromEnv()
    {
        var opts = HostStartupOptions.From(k => k == "DW3_DIALOGUE_PROBE" ? "1" : null);
        Assert.True(opts.DialogueProbe);
        Assert.False(opts.InputProbe);
    }

    [Fact]
    public void BattleHudProbeParsedFromEnv()
    {
        var opts = HostStartupOptions.From(k => k == "DW3_BATTLE_HUD_PROBE" ? "1" : null);
        Assert.True(opts.BattleHudProbe);
        Assert.False(opts.DialogueProbe);
    }

    [Fact]
    public void SqliteProbeParsedFromEnv()
    {
        var opts = HostStartupOptions.From(k => k == "DW3_SQLITE_PROBE" ? "1" : null);
        Assert.True(opts.SqliteProbe);
    }

    [Fact]
    public void WarpProbeMissingTargetParsedFromEnv()
    {
        var opts = HostStartupOptions.From(k =>
            k is "DW3_WARP_PROBE" or "DW3_WARP_PROBE_MISSING_TARGET" ? "1" : null);
        Assert.True(opts.WarpProbe);
        Assert.True(opts.WarpProbeMissingTarget);
    }

    [Fact]
    public void StartF3ProbeParsedFromEnv()
    {
        var opts = HostStartupOptions.From(k => k == "DW3_START_F3" ? "1" : null);
        Assert.True(opts.StartF3);
    }

    [Fact]
    public void CaptureAndQuitParsedFromEnv()
    {
        var opts = HostStartupOptions.From(k => k == "DW3_CAPTURE_AND_QUIT" ? "1" : null);
        Assert.True(opts.CaptureAndQuit);
    }

    // ── Probe only activates on exactly "1" ──────────────────────────────────
    [Theory]
    [InlineData("true")]
    [InlineData("yes")]
    [InlineData("0")]
    [InlineData("")]
    [InlineData("  1  ")]
    public void ProbeDoesNotActivateUnlessExactlyOne(string value)
    {
        var opts = HostStartupOptions.From(k => k == "DW3_DIALOGUE_PROBE" ? value : null);
        Assert.False(opts.DialogueProbe);
    }

    // ── String override parsing ───────────────────────────────────────────────
    [Fact]
    public void CaptureDirParsedFromEnv()
    {
        var opts = HostStartupOptions.From(k => k == "DW3_CAPTURE_DIR" ? "/tmp/caps" : null);
        Assert.Equal("/tmp/caps", opts.CaptureDir);
    }

    [Fact]
    public void ReferenceDirParsedFromEnv()
    {
        var opts = HostStartupOptions.From(k => k == "DW3_REFERENCE_DIR" ? "/ref" : null);
        Assert.Equal("/ref", opts.ReferenceDir);
    }

    [Fact]
    public void VideoDirParsedFromEnv()
    {
        var opts = HostStartupOptions.From(k => k == "DW3_VIDEO_DIR" ? "/vid" : null);
        Assert.Equal("/vid", opts.VideoDir);
    }

    [Fact]
    public void StartZoneParsedFromEnv()
    {
        var opts = HostStartupOptions.From(k => k == "DW3_START_ZONE" ? "S330" : null);
        Assert.Equal("S330", opts.StartZone);
    }

    [Fact]
    public void StartPosParsedFromEnv()
    {
        var opts = HostStartupOptions.From(k => k == "DW3_START_POS" ? "100,200" : null);
        Assert.Equal("100,200", opts.StartPos);
    }

    [Fact]
    public void StartFacingParsedFromEnv()
    {
        var opts = HostStartupOptions.From(k => k == "DW3_START_FACING" ? "3" : null);
        Assert.Equal("3", opts.StartFacing);
    }

    [Fact]
    public void RngSeedParsedFromEnv()
    {
        var opts = HostStartupOptions.From(k => k == "DW3_RNG_SEED" ? "42" : null);
        Assert.Equal("42", opts.RngSeed);
    }

    [Fact]
    public void DataDirParsedFromEnv()
    {
        var opts = HostStartupOptions.From(k => k == "DW3_DATA_DIR" ? "/data" : null);
        Assert.Equal("/data", opts.DataDir);
    }

    [Fact]
    public void PsxReferenceDirParsedFromEnv()
    {
        var opts = HostStartupOptions.From(k => k == "DW3_PSX_REFERENCE_DIR" ? "/psx" : null);
        Assert.Equal("/psx", opts.PsxReferenceDir);
    }

    [Fact]
    public void BattleActionProbeParsedFromEnv()
    {
        var opts = HostStartupOptions.From(k => k == "DW3_BATTLE_ACTION_PROBE" ? "1" : null);

        Assert.True(opts.BattleActionProbe);
    }

    [Fact]
    public void BattleModelOverridesParsedFromEnv()
    {
        var opts = HostStartupOptions.From(k => k switch
        {
            "DW3_BATTLE_MODEL" => "/m.bin",
            "DW3_BATTLE_ENEMY_MODEL" => "/e.bin",
            "DW3_BATTLE_STAGE" => "/s.bin",
            _ => null,
        });
        Assert.Equal("/m.bin", opts.BattleModel);
        Assert.Equal("/e.bin", opts.BattleEnemyModel);
        Assert.Equal("/s.bin", opts.BattleStage);
    }

    // ── FromEnvironment() smoke test ─────────────────────────────────────────
    [Fact]
    public void FromEnvironmentDoesNotThrow()
    {
        // Verifies the real factory method runs without error in a clean test process.
        var opts = HostStartupOptions.FromEnvironment();
        Assert.NotNull(opts);
    }

    // ── Multiple flags simultaneously ─────────────────────────────────────────
    [Fact]
    public void MultipleProbesCanBeActiveAtOnce()
    {
        var opts = HostStartupOptions.From(k =>
            k is "DW3_DIALOGUE_PROBE" or "DW3_ICON_PROBE" ? "1" : null);
        Assert.True(opts.DialogueProbe);
        Assert.True(opts.IconProbe);
        Assert.False(opts.InputProbe);
    }
}
