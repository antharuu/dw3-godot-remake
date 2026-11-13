using System.Text.Json;
using Dw3.Data;

namespace Dw3Production.Conversion;

/// <summary>Loads prepared battle cues and music from private pack projections, never MP archives.</summary>
public static class BattleSequenceAudioLoader
{
    /// <summary>Source sound bank of the last boss music (BOSS02, <c>MUSIC_LAST_BOSS</c>).</summary>
    private const int LastBossBank = 0x24;

    /// <summary>Source sound bank of the last boss second-form music (BOSS04, <c>MUSIC_LAST_BOSS_2</c>).</summary>
    private const int LastBossSecondBank = 0x26;

    /// <summary>Resolves the original action sound index to a validated common-bank sequence.</summary>
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
        return id != 0 && (id & 0xC0000000) == 0 && BankNumber(id) == 1 ? LoadEncoded(pack, id) : null;
    }

    /// <summary>Resolves an exclusive source music identity and requires its authored loop marker.</summary>
    /// <param name="pack">Prepared user-data pack.</param>
    /// <param name="id">Original encoded music identity.</param>
    /// <returns>A looping music sequence, or null when the identity is not music.</returns>
    public static PreparedAudioSequence? LoadMusic(DataPackReader pack, uint id)
    {
        if ((id & 0xE0000000) != 0x60000000 || BankNumber(id) is not (1 or 2 or 3 or LastBossBank or LastBossSecondBank))
        {
            return null;
        }

        var sequence = LoadEncoded(pack, id);
        return sequence.LoopSeconds is > 0 ? sequence : throw new FormatException($"Music {id:X8} has no authored loop marker.");
    }

    /// <summary>Loads one prepared sequence by its already-qualified encoded identity.</summary>
    /// <param name="pack">Prepared user-data pack.</param>
    /// <param name="id">Qualified source sequence identity.</param>
    /// <returns>The resolved prepared sequence.</returns>
    private static PreparedAudioSequence LoadEncoded(DataPackReader pack, uint id)
    {
        using var prepared = Read(pack, "sequences.json");
        using var manifest = Read(pack, "manifest.json");
        if (prepared.RootElement.GetProperty("schema").GetString() != "battle-audio-sequences-v1" ||
            manifest.RootElement.GetProperty("schema").GetString() != "battle-audio-vab-v6")
        {
            throw new FormatException("Unsupported prepared sequence or audio manifest schema.");
        }

        int bankNumber = BankNumber(id);
        JsonElement bank = Bank(prepared, BankName(bankNumber));
        int slot = (int)(id & 255);
        int group = (int)((id >> 8) & 255) + (slot / 16);
        JsonElement track = bank.GetProperty("tracks").EnumerateArray().Single(row =>
            row.GetProperty("group").GetInt32() == group && row.GetProperty("number").GetInt32() == slot % 16);
        return new BattleSequenceCompiler(pack, Bank(manifest, BankName(bankNumber)), id).Compile(track);
    }

    /// <summary>Reads the source bank number encoded in a sound identity.</summary>
    /// <param name="id">Encoded source sound identity.</param>
    /// <returns>Source sound-bank number.</returns>
    private static int BankNumber(uint id) => (int)((id >> 18) & 127);

    /// <summary>Maps supported source-bank numbers to prepared bank names.</summary>
    /// <param name="bank">Source sound-bank number.</param>
    /// <returns>Prepared bank name.</returns>
    private static string BankName(int bank) => bank switch
    {
        1 => "common",
        2 => "battle",
        3 => "boss",
        LastBossBank => "lastBoss",
        LastBossSecondBank => "lastBoss2",
        _ => throw new FormatException($"Unsupported source sound bank {bank}."),
    };

    /// <summary>Opens one required prepared private audio projection.</summary>
    /// <param name="pack">Prepared user-data pack.</param>
    /// <param name="name">Projection filename.</param>
    /// <returns>Owned JSON document.</returns>
    private static JsonDocument Read(DataPackReader pack, string name) => JsonDocument.Parse(File.ReadAllText(DataPackFiles.Require(pack, $"battle/audio/{name}", "prepared sequence audio")));

    /// <summary>Finds the named prepared bank in a projection.</summary>
    /// <param name="document">Prepared audio projection.</param>
    /// <param name="name">Expected bank name.</param>
    /// <returns>The prepared bank row.</returns>
    private static JsonElement Bank(JsonDocument document, string name) => document.RootElement.GetProperty("banks").EnumerateArray().Single(bank => bank.GetProperty("name").GetString() == name);
}
