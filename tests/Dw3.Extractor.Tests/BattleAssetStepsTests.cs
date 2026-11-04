using System.Text.Json;
using Dw3.Data;
using Dw3.Formats.Battle;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Dw3.Extractor.Tests;

/// <summary>
/// Runs the battle asset steps (HUD, report screen, battle stages, fighter models) on a fabricated
/// disc whose executable FILE_SECTORS table points engine file ids at fabricated members.
/// </summary>
public sealed class BattleAssetStepsTests : IDisposable
{
    // The frame ids the HUD step requires (two bar caps, background, gauge and the eight blink
    // frames 51..58), plus frame 64, which no rule names.
    private static readonly int[] HudFrameIds = [0, 1, 11, 30, .. Enumerable.Range(51, 8), 64];
    private static readonly int[] MenuFrameIds = [10, 57, 58, 59, 60, 61, 95, 97];

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"dw3-battle-assets-{Guid.NewGuid():N}");
    private readonly byte[] _executable = Fabricate.Executable(0x80050000);
    private readonly FabricatedDisc _disc = new();

    public BattleAssetStepsTests() => _disc.Add("SLES_039.36", _executable);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void HudStepRendersFiveClutRowsPerFrameAndTheCursorStrip()
    {
        AddEngineFile(BattleHudFiles.HudSheetFileId, "AAA/DAT/FIGHT/WFIGHTMN.BIN", FabricatedSprites.Sheet(HudFrameIds));
        AddEngineFile(BattleHudFiles.HudImagesFileId, "AAA/DAT/FIGHT/WFIGHTIM.BIN", FabricatedSprites.BattleImages());
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = Run("battle-hud", output, error);

        Assert.True(exitCode == 0, error.ToString());
        Assert.Equal("{\"event\":\"domain-complete\",\"domain\":\"battle-hud\"}" + Environment.NewLine, output.ToString());
        string hud = Path.Combine(_root, "data", "battle", "hud");
        using JsonDocument json = JsonDocument.Parse(File.ReadAllText(Path.Combine(hud, "hud.json")));
        Assert.Equal("m6-battle-hud-v3", json.RootElement.GetProperty("schema").GetString());
        Assert.Equal(HudFrameIds.Length, json.RootElement.GetProperty("frameCount").GetInt32());
        JsonElement frame = json.RootElement.GetProperty("frames").GetProperty("64");
        Assert.Equal(2, frame.GetProperty("w").GetInt32());
        Assert.Equal(5, frame.GetProperty("clutRows").GetArrayLength());
        Assert.Equal(156, json.RootElement.GetProperty("cursor").GetProperty("w").GetInt32());
        Assert.True(File.Exists(Path.Combine(hud, "frame-64-clut4.png")));
        Assert.True(File.Exists(Path.Combine(hud, "cursor-off.png")));
        using SqliteConnection db = new DataPackReader(_root).OpenDatabase();
        Assert.Equal((long)((HudFrameIds.Length * 5) + 2), Scalar(db, "SELECT count(*) FROM pack_images"));
    }

    [Fact]
    public void HudStepRejectsASheetWithoutARequiredFrame()
    {
        AddEngineFile(BattleHudFiles.HudSheetFileId, "AAA/DAT/FIGHT/WFIGHTMN.BIN", FabricatedSprites.Sheet([0, 1]));
        AddEngineFile(BattleHudFiles.HudImagesFileId, "AAA/DAT/FIGHT/WFIGHTIM.BIN", FabricatedSprites.BattleImages());
        using var error = new StringWriter();

        int exitCode = Run("battle-hud", TextWriter.Null, error);

        Assert.Equal(1, exitCode);
        Assert.Contains("Battle HUD sheet lacks required frame id 11.", error.ToString());
    }

    [Fact]
    public void HudStepRejectsDuplicateFrameIds()
    {
        AddEngineFile(BattleHudFiles.HudSheetFileId, "AAA/DAT/FIGHT/WFIGHTMN.BIN", FabricatedSprites.Sheet([.. HudFrameIds, 0]));
        AddEngineFile(BattleHudFiles.HudImagesFileId, "AAA/DAT/FIGHT/WFIGHTIM.BIN", FabricatedSprites.BattleImages());
        using var error = new StringWriter();

        int exitCode = Run("battle-hud", TextWriter.Null, error);

        Assert.Equal(1, exitCode);
        Assert.Contains("duplicate frame ids", error.ToString());
    }

    [Fact]
    public void HudStepReportsAnEngineFileThatStartsNoDiscMember()
    {
        AddEngineFile(BattleHudFiles.HudSheetFileId, "AAA/DAT/FIGHT/WFIGHTMN.BIN", FabricatedSprites.Sheet(HudFrameIds));
        using var error = new StringWriter();

        int exitCode = Run("battle-hud", TextWriter.Null, error);

        Assert.Equal(1, exitCode);
        Assert.Contains($"Engine file 0x{BattleHudFiles.HudImagesFileId:X} does not start a disc member.", error.ToString());
    }

    [Fact]
    public void ReportStepValidatesItsSourcesBeforeTheAnimationTable()
    {
        AddReportSources(reportSkip: 4);
        using var error = new StringWriter();

        int exitCode = Run("battle-report", TextWriter.Null, error);

        Assert.Equal(1, exitCode);
        Assert.Contains("STFGTREP_animations[0,0] is 0", error.ToString());
    }

    [Fact]
    public void ReportStepRejectsASheetArchiveWithAWrongOuterPointer()
    {
        AddReportSources(reportSkip: 8);
        using var error = new StringWriter();

        int exitCode = Run("battle-report", TextWriter.Null, error);

        Assert.Equal(1, exitCode);
        Assert.Contains("Report sheet archive outer pointer is 8, expected 4.", error.ToString());
    }

    [Fact]
    public void BattleStageStepRecordsAll57PopulatedUndecodableStagesAndStillCompletes()
    {
        var stages = new byte[60 * 0x58];
        for (int stageId = 0; stageId < 57; stageId++)
        {
            Fabricate.U32(stages, stageId * 0x58, 0x0300_0000);
        }

        AddEngineFile(BattleStageDecoder.FightStageFileId, "AAA/DAT/FIGHT/STAGES.BIN", stages);
        AddEngineFile(0x300, "AAA/DAT/FIGHT/STAGE00.BIN", [1, 2, 3, 4]);
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = Run("battle-stages", output, error);

        Assert.True(exitCode == 0, error.ToString());
        Assert.Contains("\"event\":\"domain-progress\",\"domain\":\"battle-stages\",\"decoded\":0,\"failed\":57", output.ToString());
        Assert.Contains("ERROR battle-stage-failed stage=0", error.ToString());
        Assert.Contains("ERROR battle-stage-failed stage=56", error.ToString());
        Assert.True(Directory.Exists(Path.Combine(_root, "data", "battle", "stages")));
    }

    [Fact]
    public void BattleModelStepRecordsAFighterWhoseModelDoesNotDecode()
    {
        var table = new byte[0x200];
        Fabricate.U32(table, 4, 0x10);
        Fabricate.U32(table, 8, 0x40);
        Fabricate.U32(table, 12, 0x100);
        Fabricate.U16(table, 0x10, 5);
        table[0x13] = 1;
        Fabricate.U16(table, 0x14, 6);
        table[0x16] = 1;
        table[0x17] = 1;
        Fabricate.U32(table, 0x40, 0x0300_0000);
        Fabricate.U32(table, 0x44, 0x0300_0000);
        Fabricate.U32(table, 0x40 + FighterModelTable.PartnerStride, 0x0301_0000);
        AddEngineFile(0x1CC, "AAA/DAT/FIGHT/FIGHTERS.BIN", table);
        AddEngineFile(0x300, "AAA/DAT/FIGHT/MODEL.BIN", [9, 9, 9, 9]);
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = Run("battle-models", output, error);

        Assert.True(exitCode == 0, error.ToString());
        Assert.Equal("{\"event\":\"domain-complete\",\"domain\":\"battle-models\",\"fighters\":2,\"failed\":2}" + Environment.NewLine, output.ToString());
        Assert.Contains("ERROR battle-model-failed fighter=5", error.ToString());
        Assert.Contains("ERROR battle-model-failed fighter=6 reason=Engine file 0x301 does not start a disc member.", error.ToString());
    }

    [Fact]
    public void BattleModelStepRejectsATruncatedFighterTable()
    {
        AddEngineFile(0x1CC, "AAA/DAT/FIGHT/FIGHTERS.BIN", [1, 2, 3]);
        using var error = new StringWriter();

        int exitCode = Run("battle-models", TextWriter.Null, error);

        Assert.Equal(1, exitCode);
        Assert.Contains("Fighter table is 3 bytes", error.ToString());
    }

    private static object? Scalar(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private void AddReportSources(uint reportSkip)
    {
        AddEngineFile(BattleReportDecoder.SheetFileId, "AAA/DAT/FIGHT/REPSHEET.BIN", FabricatedSprites.Sheet([.. BattleReportDecoder.RequiredFrameRows.Keys], reportSkip));
        AddEngineFile(BattleReportDecoder.TextureFileId, "AAA/DAT/FIGHT/REPTEX.BIN", FabricatedSprites.ReportTexture());
        AddEngineFile(BattleHudFiles.MenuSheetFileId, "AAA/DAT/COUNTRY/CMFONTCS.BIN", FabricatedSprites.Sheet(MenuFrameIds));
        AddEngineFile(BattleHudFiles.MenuTextureFileId, "AAA/DAT/COUNTRY/CMFONTTM.BIN", FabricatedSprites.MenuTexture());
        _disc.Add(BattleReportDecoder.OverlayDiscPath, new byte[0x7000]);
    }

    private void AddEngineFile(int fileId, string path, byte[] content)
    {
        _disc.Add(path, content);
        Fabricate.FileSector(_executable, fileId, _disc.LbaOf(path));
    }

    private int Run(string domain, TextWriter output, TextWriter error) =>
        Cli.Run(["extract", "--disc", "x.cue", "--out", _root, "--only", domain], output, error, _ => _disc.Open());
}
