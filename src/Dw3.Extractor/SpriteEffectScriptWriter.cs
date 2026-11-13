using System.Text.Json;
using Dw3.Data;
using Dw3.Data.Battle;
using Dw3.Disc;
using Dw3.Formats.Battle;
using Dw3.Formats.Binary;

namespace Dw3.Extractor;

/// <summary>Preserves sprite and screen-fade source signatures for every fighter/technique selector.</summary>
internal static class SpriteEffectScriptWriter
{
    /// <summary>Extracts all source selector combinations in one automated pass.</summary>
    /// <param name="context">Atomic prepared pack writer.</param>
    /// <param name="files">User-disc file resolver.</param>
    /// <param name="executable">Executable containing technique selectors.</param>
    internal static void Write(DataPackWriteContext context, EngineFileReader files, PsxExecutable executable)
    {
        FighterModelTable fighters = FighterModelTable.Parse(files.Read(0x1CC).Bytes);
        TechniqueRecord[] techniques = [.. Enumerable.Range(0, 443).Select(i => BattleDataDecoder.DecodeTechnique(
            executable.Slice(0x80042BDC + (uint)(i * BattleDataDecoder.TechniqueStride), BattleDataDecoder.TechniqueStride), 0))];
        int[] selectors = [.. techniques.Select(t => (int)t.Script).Concat([1, 2, 3, 4]).Distinct().Order()];
        var archives = new Dictionary<int, byte[]>();
        foreach (FighterModelEntry fighter in fighters.Entries)
        {
            FighterModelReference reference = fighters.Resolve(fighter.FighterId);
            foreach (int selector in selectors)
            {
                BattleActionExtraction action = BattleActionArchiveExtractor.Decode(reference, selector, files, archives);
                WriteOne(context, action, techniques, archives.TryGetValue(action.EffectFile, out byte[]? bytes) ? DiscIdentity.Sha256Bytes(bytes) : null);
            }
        }
    }

    /// <summary>Writes decoded source commands with ordinals and linked technique selectors.</summary>
    /// <param name="context">Atomic prepared pack writer.</param>
    /// <param name="action">Bounded source script or explicit unsupported result.</param>
    /// <param name="techniques">Technique source selectors in table order.</param>
    /// <param name="sourceSha256">Archive digest when source resolution succeeded.</param>
    internal static void WriteOne(DataPackWriteContext context, BattleActionExtraction action, IReadOnlyList<TechniqueRecord> techniques, string? sourceSha256)
    {
        string payload = JsonSerializer.Serialize(new
        {
            schema = "battle-sprite-scripts-v1",
            fighter = action.FighterId,
            selector = action.ScriptSelector,
            effectFile = action.EffectFile,
            effectEntry = action.EffectArchiveEntry,
            sourceSha256,
            status = action.Script is null ? "unsupported" : "decoded",
            reason = action.Reason,
            commands = action.Script?.Commands.Select((c, ordinal) => new { ordinal, opcode = (int)c.Opcode, arguments = c.Arguments })
                .Where(c => c.opcode is (int)BattleActionOpcode.SpriteEffect or (int)BattleActionOpcode.Fade).ToArray(),
            techniques = techniques.Select((t, i) => new { id = i + 1, selector = t.Script, sprite = t.ScriptEffect })
                .Where(t => t.selector == action.ScriptSelector).ToArray(),
        });
        using var command = context.Database.CreateCommand();
        command.Transaction = context.Transaction;
        command.CommandText = "INSERT INTO battle_sprite_scripts_v1 VALUES ($fighter,$selector,$payload)";
        command.Parameters.AddWithValue("$fighter", action.FighterId);
        command.Parameters.AddWithValue("$selector", action.ScriptSelector);
        command.Parameters.AddWithValue("$payload", payload);
        command.ExecuteNonQuery();
    }
}
