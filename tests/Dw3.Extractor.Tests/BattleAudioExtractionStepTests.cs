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
        JsonElement battle = manifest.RootElement.GetProperty("banks")[1];
        Assert.Equal(18, battle.GetProperty("vab").GetProperty("SampleCount").GetInt32());
        JsonElement sep = battle.GetProperty("sep");
        Assert.Equal(1, sep.GetProperty("trackCount").GetInt32());
        JsonElement track = sep.GetProperty("tracks")[0];
        Assert.Equal(0, track.GetProperty("number").GetInt32());
        Assert.Equal(2, track.GetProperty("events").GetProperty("eventCount").GetInt32());
        Assert.True(track.GetProperty("events").GetProperty("hasEndOfTrack").GetBoolean());
        Assert.Equal(
            ["number", "headerOffset", "eventsOffset", "endOffset", "ticksPerQuarterNote", "tempoMicrosecondsPerQuarterNote", "events"],
            track.EnumerateObject().Select(property => property.Name));
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
        byte[] vab = new byte[32];
        BinaryPrimitives.WriteUInt32LittleEndian(vab, 0x56414270U);
        BinaryPrimitives.WriteUInt32LittleEndian(vab.AsSpan(4), 7U);
        BinaryPrimitives.WriteUInt32LittleEndian(vab.AsSpan(12), 32U);
        BinaryPrimitives.WriteUInt16LittleEndian(vab.AsSpan(18), 11);
        BinaryPrimitives.WriteUInt16LittleEndian(vab.AsSpan(20), 23);
        BinaryPrimitives.WriteUInt16LittleEndian(vab.AsSpan(22), 18);
        vab[24] = 120;
        vab[25] = 64;
        byte[] sep = new byte[26];
        BinaryPrimitives.WriteUInt32BigEndian(sep, 0x70514553U);
        BinaryPrimitives.WriteUInt16BigEndian(sep.AsSpan(8), 0x01E0);
        sep[10] = 0x02;
        sep[11] = 0x49;
        sep[12] = 0xF0;
        sep[13] = 0x04;
        sep[14] = 0x04;
        sep[19] = 0x00;
        sep[20] = 0xC1;
        sep[21] = 0x07;
        sep[22] = 0x00;
        sep[23] = 0xFF;
        sep[24] = 0x2F;
        sep[25] = 0x00;
        byte[] archive = new byte[8 + vab.Length + sep.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(archive, 8U);
        BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(4), (uint)(8 + vab.Length));
        vab.CopyTo(archive, 8);
        sep.CopyTo(archive, 8 + vab.Length);
        return archive;
    }
}
