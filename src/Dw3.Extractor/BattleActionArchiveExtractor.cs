using Dw3.Data;
using Dw3.Disc;
using Dw3.Formats.Battle;
using Dw3.Formats.Binary;

namespace Dw3.Extractor;

/// <summary>Resolves fighter action archives and records a bounded first batch or its explicit unsupported reason.</summary>
internal static class BattleActionArchiveExtractor
{
    /// <summary>Writes one action-archive result for every fighter in the source fighter table.</summary>
    /// <param name="context">Data-pack write context.</param>
    /// <param name="disc">Open source disc.</param>
    /// <param name="entries">Source disc file list.</param>
    /// <param name="executable">Boot executable containing the engine file table.</param>
    /// <param name="fighterTableFileId">Engine file id of the fighter table.</param>
    internal static void Write(DataPackWriteContext context, IDiscSource disc, IReadOnlyList<DiscFileEntry> entries, PsxExecutable executable, int fighterTableFileId)
    {
        var files = new EngineFileReader(disc, entries, executable);
        FighterModelTable fighters = FighterModelTable.Parse(files.Read(fighterTableFileId).Bytes);
        var archives = new Dictionary<int, byte[]>();
        var actions = new List<BattleActionExtraction>();
        foreach (FighterModelEntry fighter in fighters.Entries)
        {
            FighterModelReference reference = fighters.Resolve(fighter.FighterId);
            actions.Add(Decode(reference, files, archives));
        }

        BattleActionDomainWriter.Write(context, actions);
    }

    /// <summary>Decodes one selected fighter action entry, retaining a user-actionable reason on unsupported input.</summary>
    /// <param name="reference">Resolved fighter action archive reference.</param>
    /// <param name="files">Engine file resolver.</param>
    /// <param name="archives">Cached archive bytes by engine file id.</param>
    /// <returns>The decoded script or its explicit failure reason.</returns>
    private static BattleActionExtraction Decode(FighterModelReference reference, EngineFileReader files, IDictionary<int, byte[]> archives)
    {
        try
        {
            byte[] archive = ReadArchive(reference.EffectFile, files, archives);
            BattleActionScript script = BattleActionScriptDecoder.Decode(archive, reference.EffectEntry);
            return new BattleActionExtraction(reference.FighterId, reference.EffectFile, reference.EffectEntry, script, null);
        }
        catch (FormatException error)
        {
            return new BattleActionExtraction(reference.FighterId, reference.EffectFile, reference.EffectEntry, null, error.Message);
        }
    }

    /// <summary>Loads and caches one action archive by its engine file id.</summary>
    /// <param name="fileId">Engine file id of the action archive.</param>
    /// <param name="files">Engine file resolver.</param>
    /// <param name="archives">Cached archive bytes by engine file id.</param>
    /// <returns>The raw action archive bytes.</returns>
    private static byte[] ReadArchive(int fileId, EngineFileReader files, IDictionary<int, byte[]> archives)
    {
        if (archives.TryGetValue(fileId, out byte[]? archive))
        {
            return archive;
        }

        archive = files.Read(fileId).Bytes;
        archives.Add(fileId, archive);
        return archive;
    }
}
