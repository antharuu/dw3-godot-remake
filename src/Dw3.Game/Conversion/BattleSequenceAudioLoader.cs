using System.Text.Json;
using Dw3.Data;

namespace Dw3Production.Conversion;

/// <summary>Loads non-exclusive sequence cues from prepared events, never from MP archives.</summary>
public static class BattleSequenceAudioLoader
{
    /// <summary>Resolves the original sound index to a validated prepared non-music sequence.</summary>
    /// <param name="pack">Prepared user-data pack.</param>
    /// <param name="index">Action sound index.</param>
    /// <returns>The resolved sequence or null for a non-sequence/control identifier.</returns>
    public static PreparedAudioSequence? Load(DataPackReader pack, int index)
    {
        using var sounds = Read(pack, "sounds.json");
        var ids = sounds.RootElement.GetProperty("ids");
        if (index < 0 || index >= ids.GetArrayLength())
        {
            return null;
        }

        uint id = ids[index].GetUInt32();
        if (id == 0 || (id & 0xC0000000) != 0 || ((id >> 18) & 127) != 1)
        {
            return null;
        }

        using var prepared = Read(pack, "sequences.json");
        using var manifest = Read(pack, "manifest.json");
        if (prepared.RootElement.GetProperty("schema").GetString() != "battle-audio-sequences-v1")
        {
            throw new FormatException("Unsupported prepared sequence schema.");
        }

        var tracks = Common(prepared).GetProperty("tracks");
        int slot = (int)(id & 255);
        int group = (int)((id >> 8) & 255) + (slot / 16);
        var track = tracks.EnumerateArray().Single(row => row.GetProperty("group").GetInt32() == group && row.GetProperty("number").GetInt32() == slot % 16);
        return new BattleSequenceCompiler(pack, Common(manifest), id).Compile(track);
    }

    /// <summary>Reads a required private audio projection from the pack.</summary>
    /// <param name="pack">Validated pack reader.</param>
    /// <param name="name">Relative audio file name.</param>
    /// <returns>Owned JSON document.</returns>
    private static JsonDocument Read(DataPackReader pack, string name) => JsonDocument.Parse(File.ReadAllText(DataPackFiles.Require(pack, $"battle/audio/{name}", "prepared sequence audio")));

    /// <summary>Selects the bank with the original common-bank identity.</summary>
    /// <param name="document">Prepared projection.</param>
    /// <returns>Common-bank row.</returns>
    private static JsonElement Common(JsonDocument document) => document.RootElement.GetProperty("banks").EnumerateArray().Single(bank => bank.GetProperty("name").GetString() == "common");
}
