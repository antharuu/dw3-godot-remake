using System.Text.Json;
using Dw3.Data;
using Dw3.Formats.Encounter;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Dw3.Extractor.Tests;

/// <summary>Runs the text, font and encounter steps on fabricated discs.</summary>
public sealed class TextAndEncounterStepsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"dw3-text-encounter-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void TextStepWritesEveryTalkFileWithGlyphIconAndControlTokens()
    {
        var disc = new FabricatedDisc();
        for (int index = 0; index < 10; index++)
        {
            disc.Add($"AAA/DAT/COUNTRY/FRA/FSTALK{index:D2}.BIN", Fabricate.TextTable([0x12, 0x01, 0x2A, 0x02, 0x01], [0x13]));
        }

        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = Run(disc, "text", output, error);

        Assert.True(exitCode == 0, error.ToString());
        Assert.Equal("{\"event\":\"domain-complete\",\"domain\":\"text\",\"files\":10,\"total_entries\":20}" + Environment.NewLine, output.ToString());
        using SqliteConnection db = new DataPackReader(_root).OpenDatabase();
        Assert.Equal(10L, Scalar(db, "SELECT count(*) FROM text_files WHERE entry_count = 2"));
        using JsonDocument talk = JsonDocument.Parse(File.ReadAllText(Path.Combine(_root, "data", "text", "talk03.json")));
        Assert.Equal(2, talk.RootElement.GetProperty("count").GetInt32());
        JsonElement first = talk.RootElement.GetProperty("entries")[0];
        Assert.Equal("{\"t\":\"g\",\"c\":18}", first[0].GetRawText());
        Assert.Equal("{\"t\":\"i\",\"c\":42}", first[1].GetRawText());
        Assert.Equal("{\"t\":\"c\",\"cmd\":1,\"args\":[]}", first[2].GetRawText());
    }

    [Fact]
    public void TextStepFailsOnTheFirstMissingTalkFile()
    {
        var disc = new FabricatedDisc().Add("AAA/DAT/COUNTRY/FRA/FSTALK00.BIN", Fabricate.TextTable([0x12]));
        using var error = new StringWriter();

        int exitCode = Run(disc, "text", TextWriter.Null, error);

        Assert.Equal(1, exitCode);
        Assert.Contains("Extraction error (text):", error.ToString());
        Assert.Contains("FSTALK01.BIN", error.ToString());
        Assert.False(File.Exists(Path.Combine(_root, "data", "dw3.sqlite")));
    }

    [Fact]
    public void FontStepRejectsAnExecutableWhoseStyleTableDoesNotMatchTheProfile()
    {
        var disc = new FabricatedDisc()
            .Add("SLES_039.36", Fabricate.Executable(0x80050000))
            .Add("AAA/DAT/COUNTRY/CMFONTTM.BIN", FabricatedSprites.MenuTexture());
        using var error = new StringWriter();

        int exitCode = Run(disc, "font", TextWriter.Null, error);

        Assert.Equal(1, exitCode);
        Assert.Contains("Extraction error (font): Style 1 line height 0", error.ToString());
    }

    [Fact]
    public void EncounterStepDecodesTheFixedTablesAndSkipsOverlaysWithoutBattleArrays()
    {
        var disc = new FabricatedDisc()
            .Add("AAA/PRO/FIELDSTG.PRO", FieldStage(validEnemyPointers: true))
            .Add("AAA/PRO/WSTAG200.PRO", new byte[0x400])
            .Add("AAA/PRO/WSTAGX.PRO", new byte[0x400]);
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = Run(disc, "encounter", output, error);

        Assert.True(exitCode == 0, error.ToString());
        Assert.Equal(
            $"{{\"event\":\"domain-complete\",\"domain\":\"encounter\",\"enemies\":{FieldEncounterDecoder.EnemyCount},\"encounters\":{FieldEncounterDecoder.EncounterCount},\"stages\":0}}" + Environment.NewLine,
            output.ToString());
        using SqliteConnection db = new DataPackReader(_root).OpenDatabase();
        Assert.True(Scalar(db, "SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name LIKE 'encounter%'") is long tables && tables > 0);
    }

    [Fact]
    public void EncounterStepRejectsAnEncounterThatPointsOutsideTheEnemyTable()
    {
        var disc = new FabricatedDisc().Add("AAA/PRO/FIELDSTG.PRO", FieldStage(validEnemyPointers: false));
        using var error = new StringWriter();

        int exitCode = Run(disc, "encounter", TextWriter.Null, error);

        Assert.Equal(1, exitCode);
        Assert.StartsWith("Extraction error [encounter]:", error.ToString());
    }

    /// <summary>
    /// Builds a zero-filled FIELDSTG.PRO large enough for the battle-rate table. With
    /// <paramref name="validEnemyPointers"/>, every encounter slot points at enemy 0 so the
    /// encounters resolve.
    /// </summary>
    private static byte[] FieldStage(bool validEnemyPointers)
    {
        uint end = FieldEncounterDecoder.BattleRatesAddr + (FieldEncounterDecoder.BattleRatesCount * 4);
        var data = new byte[end - FieldEncounterDecoder.FieldOverlayBase];
        if (!validEnemyPointers)
        {
            return data;
        }

        int encounters = (int)(FieldEncounterDecoder.EncountersAddr - FieldEncounterDecoder.FieldOverlayBase);
        for (int index = 0; index < FieldEncounterDecoder.EncounterCount; index++)
        {
            for (int slot = 0; slot < 3; slot++)
            {
                Fabricate.U32(data, encounters + (index * FieldEncounterDecoder.EncounterStride) + (slot * 4), FieldEncounterDecoder.EnemiesAddr);
            }
        }

        return data;
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
