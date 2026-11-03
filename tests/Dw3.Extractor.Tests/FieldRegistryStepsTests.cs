using System.Text.Json;
using Dw3.Data;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Dw3.Extractor.Tests;

/// <summary>
/// Runs the steps that start from the field-stage registry (zones, water, dialogue, NPCs read from
/// the disc tables) on a fabricated disc of 236 stages built by <see cref="FabricatedFieldDisc"/>.
/// </summary>
public sealed class FieldRegistryStepsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"dw3-registry-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void ZonesStepWritesTerrainCellMapsAndManifestOfEveryStageWithAPacket()
    {
        FabricatedDisc disc = FabricatedFieldDisc.Create();
        foreach (int stage in new[] { 200, 232, 261, 376 })
        {
            disc.Add($"AAA/DAT/Z_STAGE/S{stage:D3}PACK.BIN", TerrainPacket());
            disc.Add($"AAA/DAT/FIELD/STAGE/S{stage:D3}TMPK.BIN", TmpkArchive());
        }

        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = Run(disc, "zones", output, error);

        Assert.True(exitCode == 0, error.ToString());
        Assert.Contains("{\"event\":\"zone-complete\",\"stage\":200,\"width\":128,\"height\":128}", output.ToString());
        Assert.Matches("\"event\":\"domain-complete\",\"domain\":\"zones\",\"stages\":4,", output.ToString());
        using SqliteConnection db = new DataPackReader(_root).OpenDatabase();
        Assert.Equal(4L, Scalar(db, "SELECT count(*) FROM zones WHERE width_pixels = 128 AND height_pixels = 128"));
        Assert.Equal(236L, Scalar(db, "SELECT count(*) FROM field_stages"));
        Assert.Equal(4L, Scalar(db, "SELECT count(*) FROM field_stages WHERE status = 'loadable'"));
        string zone = Path.Combine(_root, "data", "zones", "S200");
        Assert.True(File.Exists(Path.Combine(zone, "composite-back-to-front.png")));
        Assert.True(File.Exists(Path.Combine(zone, "areas-cells.png")));
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(zone, "manifest.json")));
        Assert.Contains("AAA/DAT/FIELD/STAGE/S200TMPK.BIN", manifest.RootElement.GetRawText());
    }

    [Fact]
    public void ZonesStepWithoutTerrainPacketsFailsExplicitly()
    {
        FabricatedDisc disc = FabricatedFieldDisc.Create();
        using var error = new StringWriter();

        int exitCode = Run(disc, "zones", TextWriter.Null, error);

        Assert.Equal(1, exitCode);
        Assert.Equal("Extraction error: The supported disc contains no field terrain packet files." + Environment.NewLine, error.ToString());
    }

    [Fact]
    public void ZonesStepRejectsARegistryThatDoesNotHoldTheExpectedStageCount()
    {
        FabricatedDisc disc = FabricatedFieldDisc.Create(overlay: stage => stage == 300 ? new byte[16] : FabricatedFieldDisc.Overlay());
        using var error = new StringWriter();

        int exitCode = Run(disc, "zones", TextWriter.Null, error);

        Assert.Equal(1, exitCode);
        Assert.Contains("Disc field registry yielded 235 stages instead of 236.", error.ToString());
        Assert.Contains("S300:", error.ToString());
    }

    [Fact]
    public void ZonesStepCalledForAnotherDomainIsAUsageError()
    {
        using var error = new StringWriter();

        int exitCode = ZoneExtractionStep.Extract(() => throw new InvalidOperationException("must not open"), _root, "water", TextWriter.Null, error);

        Assert.Equal(2, exitCode);
        Assert.Equal("Unsupported extraction domain: water" + Environment.NewLine, error.ToString());
    }

    [Fact]
    public void WaterStepRecordsEveryStageWhoseObjectsDoNotDecodeAsSkipped()
    {
        FabricatedDisc disc = FabricatedFieldDisc.Create(stageSprites: true);
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = Run(disc, "water", output, error);

        Assert.True(exitCode == 0, error.ToString());
        string[] lines = output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(237, lines.Length);
        Assert.Equal("{\"event\":\"zone-skipped\",\"domain\":\"water\",\"stage\":200}", lines[0]);
        Assert.Equal("{\"event\":\"domain-complete\",\"domain\":\"water\",\"stages\":0,\"skipped\":236,\"frames\":0}", lines[^1]);
    }

    [Fact]
    public void NpcStepReadsTheActorTablesFromTheDiscAndVisitsEveryRegisteredStage()
    {
        FabricatedDisc disc = FabricatedFieldDisc.Create();
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = Run(disc, "npc", output, error);

        Assert.True(exitCode == 0, error.ToString());
        Assert.Equal("{\"event\":\"domain-complete\",\"domain\":\"npc\",\"zones\":237,\"visible_npcs\":0,\"decoded\":0}" + Environment.NewLine, output.ToString());
    }

    [Fact]
    public void DialogueStepReportsAStageWhoseSetupCodeSelectsNoTextFile()
    {
        FabricatedDisc disc = FabricatedFieldDisc.Create();
        using var error = new StringWriter();

        int exitCode = Run(disc, "dialogue", TextWriter.Null, error);

        Assert.Equal(1, exitCode);
        Assert.Contains("Stage mode 0x204 (WSTAG199): Setup code must store exactly one language-relative text file", error.ToString());
    }

    [Fact]
    public void AnalyzeDialogueCommandListsEveryStageThatFailsTheAnalysis()
    {
        FabricatedDisc disc = FabricatedFieldDisc.Create();
        using var output = new StringWriter();

        int exitCode = Cli.Run(["analyze-dialogue", "--disc", "x.cue", "--details"], output, TextWriter.Null, _ => disc.Open());

        Assert.Equal(0, exitCode);
        string[] lines = output.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);
        Assert.StartsWith("dialogue-analysis stages=237 analysed=0 failed=237 ", lines[0]);
        Assert.Equal(238, lines.Length);
        Assert.StartsWith("failed Stage mode 0x204 (WSTAG199):", lines[1]);
        Assert.Equal(0, disc.OpenHandles);
    }

    /// <summary>
    /// One-tile terrain packet: header (present count 1, tile count 1, one column, 2048-byte tiles,
    /// tile 0 present), then a tile with one 2×1 sprite and an 8-bit TIM whose palette entry 1 is red.
    /// </summary>
    private static byte[] TerrainPacket()
    {
        const int sector = 2048;
        const int textureOffset = 40;
        var file = new byte[sector * 2];
        Fabricate.U32(file, 0, 1);
        Fabricate.U32(file, 4, 1);
        Fabricate.U32(file, 8, 1);
        Fabricate.U32(file, 12, sector);
        Fabricate.U16(file, 16, 1);
        Fabricate.U32(file, sector, textureOffset);
        Fabricate.U32(file, sector + 4, 1);
        Fabricate.U16(file, sector + 16, 2);
        Fabricate.U16(file, sector + 18, 1);
        int tim = sector + textureOffset;
        Fabricate.U32(file, tim, 0x10);
        Fabricate.U32(file, tim + 4, 0x09);
        Fabricate.U32(file, tim + 8, 12 + 512);
        Fabricate.U16(file, tim + 16, 256);
        Fabricate.U16(file, tim + 18, 1);
        Fabricate.U16(file, tim + 22, 0x001F);
        int image = tim + 8 + 12 + 512;
        Fabricate.U32(file, image, 14);
        Fabricate.U16(file, image + 8, 1);
        Fabricate.U16(file, image + 10, 1);
        file[image + 12] = 1;
        file[image + 13] = 1;
        return file;
    }

    /// <summary>
    /// TMPK archive of six identical 1×1-tile (128×128 pixel) maps: each entry holds the six
    /// sub-offsets of its grid, cell levels and 64 pixel bytes of value 0x21.
    /// </summary>
    private static byte[] TmpkArchive()
    {
        const int entries = 6;
        const int entryBytes = 55 + 64;
        int first = entries * 4;
        var archive = new byte[first + (entries * entryBytes)];
        for (int index = 0; index < entries; index++)
        {
            int entry = first + (index * entryBytes);
            Fabricate.U32(archive, index * 4, (uint)entry);
            int[] subOffsets = [24, 27, 31, 39, 47, 55];
            for (int sub = 0; sub < subOffsets.Length; sub++)
            {
                Fabricate.U32(archive, entry + (sub * 4), (uint)subOffsets[sub]);
            }

            archive[entry + 24] = 1;
            archive[entry + 25] = 1;
            archive.AsSpan(entry + 55, 64).Fill(0x21);
        }

        return archive;
    }

    private static object? Scalar(SqliteConnection connection, string sql)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private int Run(FabricatedDisc disc, string domain, TextWriter output, TextWriter error) =>
        Cli.Run(["extract", "--disc", "x.cue", "--out", _root, "--only", domain], output, error, _ => disc.Open());
}
