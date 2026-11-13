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
        Assert.Equal("battle-audio-vab-v6", manifest.RootElement.GetProperty("schema").GetString());
        Assert.Equal(5, manifest.RootElement.GetProperty("banks").GetArrayLength());
        Assert.Equal(["common", "battle", "boss", "lastBoss", "lastBoss2"], manifest.RootElement.GetProperty("banks").EnumerateArray().Select(bank => bank.GetProperty("name").GetString()));
        JsonElement battle = manifest.RootElement.GetProperty("banks")[1];
        Assert.Equal(1, battle.GetProperty("vab").GetProperty("SampleCount").GetInt32());
        JsonElement sep = battle.GetProperty("sep");
        Assert.Equal(1, sep.GetProperty("trackCount").GetInt32());
        JsonElement track = sep.GetProperty("tracks")[0];
        Assert.Equal(0, track.GetProperty("number").GetInt32());
        using var playback = JsonDocument.Parse(File.ReadAllText(Path.Combine(audio, "sequences.json")));
        var midi = playback.RootElement.GetProperty("banks")[0].GetProperty("tracks")[0].GetProperty("events")[0];
        Assert.Equal(0xC1, midi.GetProperty("Status").GetInt32());
        Assert.Equal(7, midi.GetProperty("Data1").GetInt32());
        Assert.Equal(4, track.GetProperty("events").GetProperty("eventCount").GetInt32());
        Assert.True(track.GetProperty("events").GetProperty("hasEndOfTrack").GetBoolean());
        Assert.Equal(
            ["number", "headerOffset", "eventsOffset", "endOffset", "ticksPerQuarterNote", "tempoMicrosecondsPerQuarterNote", "events"],
            track.EnumerateObject().Select(property => property.Name));
        Assert.Equal(1, manifest.RootElement.GetProperty("banks")[1].GetProperty("samples").GetArrayLength());
        var catalog = Dw3Production.Conversion.BattleActionAudioLoader.Load(new Dw3.Data.DataPackReader(_root));
        var action = Assert.Single(catalog);
        Assert.Equal(52, action.Key);
        Assert.Equal(0, action.Value.Program);
        Assert.Equal(0, action.Value.Tone);
        Assert.Equal(0, action.Value.SampleIndex);
        Assert.Equal(60, action.Value.Note);
        Assert.Equal(0, action.Value.Priority);
        Assert.True(action.Value.Reverb);
        var cue = Dw3Production.Conversion.BattleSequenceAudioLoader.Load(new Dw3.Data.DataPackReader(_root), 10);
        Assert.NotNull(cue);
        var note = cue.Commands[0].Entry;
        Assert.NotNull(note);
        Assert.Equal(7, note.Program);
        Assert.Equal(60, note.Note);
        Assert.Equal(0x40000U, note.EncodedId);
        Assert.False(note.Reverb);
        Assert.Null(cue.Commands[1].Entry);
        Assert.Equal(120 * 150000.0 / 480 / 1000000, cue.EndSeconds);
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
        byte[] overlay = new byte[Dw3.Formats.Battle.BattleSoundTableDecoder.TableOffset + (92 * 4)];
        BinaryPrimitives.WriteUInt32LittleEndian(overlay.AsSpan(0x40B4), 0x3C048004);
        BinaryPrimitives.WriteUInt32LittleEndian(overlay.AsSpan(0x40C4), 0x0040F809);
        BinaryPrimitives.WriteUInt32LittleEndian(overlay.AsSpan(0x40C8), 0x3484003C);
        BinaryPrimitives.WriteUInt32LittleEndian(overlay.AsSpan(Dw3.Formats.Battle.BattleSoundTableDecoder.TableOffset + (10 * 4)), 0x40000);
        BinaryPrimitives.WriteUInt32LittleEndian(overlay.AsSpan(Dw3.Formats.Battle.BattleSoundTableDecoder.TableOffset + (52 * 4)), 0x8004003C);
        disc.Add("AAA/PRO/FIGHTSTG.PRO", overlay);
        disc.Add("AAA/DAT/SOUND/COMMON/MPCOMMON.BIN", HeaderArchive());
        disc.Add("AAA/DAT/SOUND/COMMON/MVCOMMON.BIN", BodyArchive());
        disc.Add("AAA/DAT/SOUND/BATL00/MPBATL00.BIN", HeaderArchive());
        disc.Add("AAA/DAT/SOUND/BATL00/MVBATL00.BIN", BodyArchive());
        disc.Add("AAA/DAT/SOUND/BOSS00/MPBOSS00.BIN", HeaderArchive());
        disc.Add("AAA/DAT/SOUND/BOSS00/MVBOSS00.BIN", BodyArchive());
        disc.Add("AAA/DAT/SOUND/BOSS02/MPBOSS02.BIN", HeaderArchive());
        disc.Add("AAA/DAT/SOUND/BOSS02/MVBOSS02.BIN", BodyArchive());
        disc.Add("AAA/DAT/SOUND/BOSS04/MPBOSS04.BIN", HeaderArchive());
        disc.Add("AAA/DAT/SOUND/BOSS04/MVBOSS04.BIN", BodyArchive());
    }

    private static byte[] HeaderArchive()
    {
        const int vabBytes = 0x2020;
        byte[] sep = new byte[34];
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
        new byte[] { 0, 0x91, 60, 100, 120, 60, 0, 0, 0xFF, 0x2F, 0 }.CopyTo(sep, 22);
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
        for (int program = 0; program < 11; program++)
        {
            vab[32 + (program * 16)] = 1;
            vab[33 + (program * 16)] = 127;
            vab[36 + (program * 16)] = 64;
            int toneOffset = 2080 + (program * 512);
            vab[toneOffset + 1] = program == 0 ? (byte)4 : (byte)0;
            vab[toneOffset + 2] = 127;
            vab[toneOffset + 3] = 64;
            vab[toneOffset + 4] = 60;
            vab[toneOffset + 7] = 127;
            BinaryPrimitives.WriteInt16LittleEndian(vab.Slice(toneOffset + 22), 1);
        }

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
        body[6] = 0x11;
        body[20] = 12;
        body[21] = 1;
        return body;
    }
}
