using System.Text.Json;
using Dw3.Data;

namespace Dw3Production.Conversion;

/// <summary>Loads the separately extracted entrance key-on and its source-clock deadline.</summary>
internal static class BattleEntranceAudioLoader
{
    /// <summary>Resolves the direct cue against its real VAB program, tone, note and sample.</summary>
    /// <param name="pack">Prepared user-data pack.</param>
    /// <returns>Delay and fully resolved direct note.</returns>
    public static (int Delay, AudioProgramEntry Entry) Load(DataPackReader pack)
    {
        using var cue = JsonDocument.Parse(File.ReadAllText(DataPackFiles.Require(pack, "battle/audio/entrance.json", "entrance cue")));
        using var manifest = JsonDocument.Parse(File.ReadAllText(DataPackFiles.Require(pack, "battle/audio/manifest.json", "entrance bank")));
        uint id = cue.RootElement.GetProperty("id").GetUInt32();
        int delay = cue.RootElement.GetProperty("delayFrames").GetInt32();
        if (cue.RootElement.GetProperty("schema").GetString() != "battle-entrance-audio-v1" || (id & 0x80000000) == 0 || ((id >> 18) & 127) != 1 || delay < 0)
        {
            throw new FormatException("Invalid prepared entrance cue.");
        }

        var bank = manifest.RootElement.GetProperty("banks").EnumerateArray().Single(row => row.GetProperty("name").GetString() == "common");
        var entry = BattleActionAudioLoader.ResolveTone(pack, bank, id);
        if (entry.Adsr1 is null || entry.Adsr2 is null)
        {
            throw new FormatException("Entrance cue requires prepared ADSR attributes.");
        }

        return (delay, entry);
    }
}
