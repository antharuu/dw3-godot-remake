using System.Buffers.Binary;
using System.Text.Json;
using Xunit;

namespace Dw3.Extractor.Tests;

/// <summary>Exercises battle-audio extraction with a fabricated VAB archive and body for each required bank.</summary>
public sealed class BattleAudioExtractionStepTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"dw3-battle-audio-{Guid.NewGuid():N}");

    [Fact]
    public void Extract_WritesDecodedVabMetadataAndPrivateBankPairs()
    {
        var disc = new FabricatedDisc();
        AddBanks(disc);
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exitCode = Cli.Run(["extract", "--disc", "x.cue", "--out", _root, "--only", "battle-audio"], output, error, _ => disc.Open());

        Assert.True(exitCode == 0, error.ToString());
        Assert.Equal("{\"event\":\"domain-complete\",\"domain\":\"battle-audio\"}" + Environment.NewLine, output.ToString());
        string audio = Path.Combine(_root, "data", "battle", "audio");
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(audio, "manifest.json")));
        Assert.Equal("battle-audio-vab-v1", manifest.RootElement.GetProperty("schema").GetString());
        Assert.Equal(3, manifest.RootElement.GetProperty("banks").GetArrayLength());
        Assert.Equal(18, manifest.RootElement.GetProperty("banks")[1].GetProperty("vab").GetProperty("SampleCount").GetInt32());
        Assert.True(File.Exists(Path.Combine(audio, "battle.mp")));
        Assert.True(File.Exists(Path.Combine(audio, "battle.mv")));
    }

    [Fact]
    public void Extract_RejectsMissingBattleBody()
    {
        var disc = new FabricatedDisc();
        AddBanks(disc);
        disc = new FabricatedDisc().Add("AAA/DAT/SOUND/COMMON/MPCOMMON.BIN", HeaderArchive()).Add("AAA/DAT/SOUND/COMMON/MVCOMMON.BIN", [1]);
        using var error = new StringWriter();

        int exitCode = Cli.Run(["extract", "--disc", "x.cue", "--out", _root, "--only", "battle-audio"], error, error, _ => disc.Open());

        Assert.Equal(1, exitCode);
        Assert.Contains("Required disc member missing", error.ToString());
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private static void AddBanks(FabricatedDisc disc)
    {
        disc.Add("AAA/DAT/SOUND/COMMON/MPCOMMON.BIN", HeaderArchive());
        disc.Add("AAA/DAT/SOUND/COMMON/MVCOMMON.BIN", [1]);
        disc.Add("AAA/DAT/SOUND/BATL00/MPBATL00.BIN", HeaderArchive());
        disc.Add("AAA/DAT/SOUND/BATL00/MVBATL00.BIN", [2]);
        disc.Add("AAA/DAT/SOUND/BOSS00/MPBOSS00.BIN", HeaderArchive());
        disc.Add("AAA/DAT/SOUND/BOSS00/MVBOSS00.BIN", [3]);
    }

    private static byte[] HeaderArchive()
    {
        byte[] archive = new byte[40];
        BinaryPrimitives.WriteUInt32LittleEndian(archive, 8U);
        BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(8), 0x56414270U);
        BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(12), 7U);
        BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(20), 32U);
        BinaryPrimitives.WriteUInt16LittleEndian(archive.AsSpan(26), 11);
        BinaryPrimitives.WriteUInt16LittleEndian(archive.AsSpan(28), 23);
        BinaryPrimitives.WriteUInt16LittleEndian(archive.AsSpan(30), 18);
        archive[32] = 120;
        archive[33] = 64;
        return archive;
    }
}
