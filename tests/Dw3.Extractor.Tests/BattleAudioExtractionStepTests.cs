using System.Buffers.Binary;
using System.Text.Json;
using Xunit;

namespace Dw3.Extractor.Tests;

/// <summary>Exercises battle-audio extraction with fabricated VAB headers and SPU ADPCM body samples.</summary>
public sealed class BattleAudioExtractionStepTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"dw3-battle-audio-{Guid.NewGuid():N}");

    [Fact]
    public void Extract_WritesDecodedPcmWavesAndPrivateBankPairs()
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
        Assert.Equal("battle-audio-vab-v2", manifest.RootElement.GetProperty("schema").GetString());
        Assert.Equal(3, manifest.RootElement.GetProperty("banks").GetArrayLength());
        JsonElement battle = manifest.RootElement.GetProperty("banks")[1];
        Assert.Equal(1, battle.GetProperty("vab").GetProperty("SampleCount").GetInt32());
        JsonElement sep = battle.GetProperty("sep");
        Assert.Equal(1, sep.GetProperty("trackCount").GetInt32());
        JsonElement track = sep.GetProperty("tracks")[0];
        Assert.Equal(0, track.GetProperty("number").GetInt32());
        Assert.Equal(2, track.GetProperty("events").GetProperty("eventCount").GetInt32());
        Assert.True(track.GetProperty("events").GetProperty("hasEndOfTrack").GetBoolean());
        Assert.Equal(
            ["number", "headerOffset", "eventsOffset", "endOffset", "ticksPerQuarterNote", "tempoMicrosecondsPerQuarterNote", "events"],
            track.EnumerateObject().Select(property => property.Name));
        Assert.Equal(1, manifest.RootElement.GetProperty("banks")[1].GetProperty("samples").GetArrayLength());
        Assert.True(File.Exists(Path.Combine(audio, "battle.mp")));
        Assert.True(File.Exists(Path.Combine(audio, "battle.mv")));
        Assert.Equal("RIFF", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(Path.Combine(audio, "battle", "000.wav")), 0, 4));
    }

    [Fact]
    public void Extract_RejectsMissingBattleBody()
    {
        var disc = new FabricatedDisc();
        AddBanks(disc);
        disc = new FabricatedDisc().Add("AAA/DAT/SOUND/COMMON/MPCOMMON.BIN", HeaderArchive()).Add("AAA/DAT/SOUND/COMMON/MVCOMMON.BIN", BodyArchive());
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
        disc.Add("AAA/DAT/SOUND/COMMON/MVCOMMON.BIN", BodyArchive());
        disc.Add("AAA/DAT/SOUND/BATL00/MPBATL00.BIN", HeaderArchive());
        disc.Add("AAA/DAT/SOUND/BATL00/MVBATL00.BIN", BodyArchive());
        disc.Add("AAA/DAT/SOUND/BOSS00/MPBOSS00.BIN", HeaderArchive());
        disc.Add("AAA/DAT/SOUND/BOSS00/MVBOSS00.BIN", BodyArchive());
    }

    private static byte[] HeaderArchive()
    {
        const int vabBytes = 0x2020;
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
        byte[] archive = new byte[8 + vabBytes + sep.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(archive, 8U);
        BinaryPrimitives.WriteUInt32LittleEndian(archive.AsSpan(4), 8U + vabBytes);
        Span<byte> vab = archive.AsSpan(8, vabBytes);
        BinaryPrimitives.WriteUInt32LittleEndian(vab, 0x56414270U);
        BinaryPrimitives.WriteUInt32LittleEndian(vab.Slice(4), 7U);
        BinaryPrimitives.WriteUInt32LittleEndian(vab.Slice(12), 32U);
        BinaryPrimitives.WriteUInt16LittleEndian(vab.Slice(18), 11);
        BinaryPrimitives.WriteUInt16LittleEndian(vab.Slice(20), 23);
        BinaryPrimitives.WriteUInt16LittleEndian(vab.Slice(22), 1);
        vab[24] = 120;
        vab[25] = 64;
        BinaryPrimitives.WriteUInt16LittleEndian(vab.Slice(vabBytes - 510), 4);
        sep.CopyTo(archive, 8 + vabBytes);
        return archive;
    }

    private static byte[] BodyArchive()
    {
        byte[] body = new byte[36];
        BinaryPrimitives.WriteUInt32LittleEndian(body, 4U);
        body[4] = 12;
        body[5] = 0;
        body[20] = 12;
        body[21] = 1;
        return body;
    }
}
