using System.Text.Json;
using Dw3.Data;

namespace Dw3Production.Conversion;

/// <summary>Resolves script indices through extracted encoded IDs and explicit VAB tone slots.</summary>
public static class BattleActionAudioLoader
{
    /// <summary>VAB tone mode bit that libsnd copies into the SPU reverb-voice mask at key-on.</summary>
    private const int ReverbModeBit = 4;

    /// <summary>Loads action key-on sounds from the prepared pack. SEP entries remain owned by music playback.</summary>
    /// <param name="pack">Validated local pack, never a disc reader.</param>
    /// <returns>Resolved script-index catalog.</returns>
    public static IReadOnlyDictionary<int, AudioProgramEntry> Load(DataPackReader pack)
    {
        ArgumentNullException.ThrowIfNull(pack);
        try
        {
            using var sounds = JsonDocument.Parse(File.ReadAllText(DataPackFiles.Require(pack, "battle/audio/sounds.json", "battle sound indices")));
            using var manifest = JsonDocument.Parse(File.ReadAllText(DataPackFiles.Require(pack, "battle/audio/manifest.json", "battle audio")));
            if (sounds.RootElement.GetProperty("schema").GetString() != "battle-action-sounds-v1" ||
                manifest.RootElement.GetProperty("schema").GetString() != "battle-audio-vab-v6")
            {
                throw new FormatException("Unsupported action sound catalog. Prepare the current audio pack before playback.");
            }

            var bank = manifest.RootElement.GetProperty("banks").EnumerateArray()
                .Single(row => row.GetProperty("name").GetString() == "common");
            return Resolve(pack, bank, sounds.RootElement.GetProperty("ids"));
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException or KeyNotFoundException or OverflowException or ArgumentOutOfRangeException or IndexOutOfRangeException)
        {
            throw new FormatException($"Invalid action audio manifest: {error.Message}", error);
        }
    }

    /// <summary>Qualifies program, tone and sample independently before physical playback.</summary>
    /// <param name="pack">Prepared pack.</param>
    /// <param name="bank">Extracted bank.</param>
    /// <param name="id">Packed key-on identifier.</param>
    /// <returns>Fully resolved playback attributes.</returns>
    internal static AudioProgramEntry ResolveTone(DataPackReader pack, JsonElement bank, uint id)
    {
        int program = (int)((id >> 11) & 127);
        int tone = (int)((id >> 7) & 15);
        var row = bank.GetProperty("tones").EnumerateArray().Single(value =>
            value.GetProperty("ProgramIndex").GetInt32() == program && value.GetProperty("ToneIndex").GetInt32() == tone);
        var attributes = row.GetProperty("Attributes");
        int sampleIndex = attributes.GetProperty("SampleRef").GetInt32() - 1;
        var sample = bank.GetProperty("samples")[sampleIndex];
        var header = bank.GetProperty("vab");
        return new AudioProgramEntry(
            sampleIndex,
            DataPackFiles.Require(pack, sample.GetProperty("wavPath").GetString() ?? throw new FormatException("Missing sample path."), "action sample"),
            attributes.GetProperty("Volume").GetByte(),
            attributes.GetProperty("Pan").GetByte(),
            attributes.GetProperty("CenterNote").GetByte(),
            attributes.GetProperty("PitchShift").GetSByte())
        {
            Adsr1 = attributes.TryGetProperty("Adsr1", out var first) ? first.GetUInt16() : null,
            Adsr2 = attributes.TryGetProperty("Adsr2", out var second) ? second.GetUInt16() : null,
            PitchBendDown = attributes.GetProperty("PitchBendDown").GetByte(),
            PitchBendUp = attributes.GetProperty("PitchBendUp").GetByte(),
            Reverb = (attributes.GetProperty("Mode").GetByte() & ReverbModeBit) != 0,
            EncodedId = id,
            Program = program,
            Tone = tone,
            Note = (int)(id & 127),
            Priority = attributes.GetProperty("Priority").GetInt32(),
            BankGain = header.GetProperty("MasterVolume").GetSingle() * row.GetProperty("ProgramVolume").GetSingle() / (127 * 127),
            PanOffset = (header.GetProperty("MasterPan").GetSingle() + row.GetProperty("ProgramPan").GetSingle() - 128) / 64,
            Loops = sample.GetProperty("loops").GetBoolean(),
            LoopStart = sample.GetProperty("loopStartSample").ValueKind == JsonValueKind.Null ? 0 : sample.GetProperty("loopStartSample").GetInt32(),
        };
    }

    /// <summary>Maps supported key-on IDs without inventing programs for sequence entries.</summary>
    /// <param name="pack">Prepared pack.</param>
    /// <param name="bank">Extracted common bank.</param>
    /// <param name="ids">ROM-extracted sound IDs.</param>
    /// <returns>Playable action indices only.</returns>
    private static Dictionary<int, AudioProgramEntry> Resolve(DataPackReader pack, JsonElement bank, JsonElement ids)
    {
        var result = new Dictionary<int, AudioProgramEntry>();
        int index = 0;
        foreach (var item in ids.EnumerateArray())
        {
            uint id = item.GetUInt32();
            if ((id & 0x80000000) != 0 && ((id >> 18) & 127) == 1)
            {
                result.Add(index, ResolveTone(pack, bank, id));
            }

            index++;
        }

        return result;
    }
}
