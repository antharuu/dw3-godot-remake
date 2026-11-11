using Dw3.Data;
using Dw3.Data.Battle;
using Dw3.Disc;
using Dw3.Formats.Battle;
using Dw3.Formats.Binary;

namespace Dw3.Extractor;

/// <summary>Resolves fighter action archives and records a bounded first batch or its explicit unsupported reason.</summary>
internal static class BattleActionArchiveExtractor
{
    /// <summary>
    /// Writes one action-archive result per (fighter, script-selector) pair.
    /// The effects reference in the fighter record identifies a physical archive file and the index
    /// of the outer entry that IS the fighter-specific sub-archive of action scripts.
    /// The script selector from the technique table then picks one entry from that sub-archive.
    /// </summary>
    /// <param name="context">Data-pack write context.</param>
    /// <param name="disc">Open source disc.</param>
    /// <param name="entries">Source disc file list.</param>
    /// <param name="executable">Boot executable containing the engine file table.</param>
    /// <param name="fighterTableFileId">Engine file id of the fighter table.</param>
    /// <param name="scriptSelectors">Unique script-selector values decoded from the technique table.</param>
    internal static void Write(DataPackWriteContext context, IDiscSource disc, IReadOnlyList<DiscFileEntry> entries, PsxExecutable executable, int fighterTableFileId, IReadOnlySet<int> scriptSelectors)
    {
        var files = new EngineFileReader(disc, entries, executable);
        FighterModelTable fighters = FighterModelTable.Parse(files.Read(fighterTableFileId).Bytes);
        var archives = new Dictionary<int, byte[]>();
        var actions = new List<BattleActionExtraction>();
        foreach (FighterModelEntry fighter in fighters.Entries)
        {
            FighterModelReference reference = fighters.Resolve(fighter.FighterId);
            foreach (int selector in scriptSelectors)
            {
                actions.Add(Decode(reference, selector, files, archives));
            }
        }

        BattleActionDomainWriter.Write(context, actions);
    }

    /// <summary>
    /// Decodes one action script for a given (fighter, selector) pair using the two-level archive
    /// structure: the outer archive entry at <c>EffectEntry</c> IS the fighter sub-archive, and the
    /// script selector picks the entry within that sub-archive.
    /// </summary>
    /// <param name="reference">Resolved fighter action archive reference.</param>
    /// <param name="scriptSelector">Script-selector index within the fighter effect sub-archive.</param>
    /// <param name="files">Engine file resolver.</param>
    /// <param name="archives">Cached archive bytes by engine file id.</param>
    /// <returns>The decoded script or its explicit failure reason.</returns>
    private static BattleActionExtraction Decode(FighterModelReference reference, int scriptSelector, EngineFileReader files, IDictionary<int, byte[]> archives)
    {
        try
        {
            byte[] archive = ReadArchive(reference.EffectFile, files, archives);
            byte[] subArchive = ReadSubArchive(archive, reference.EffectEntry, reference.FighterId);
            BattleActionScript script = BattleActionScriptDecoder.Decode(subArchive, scriptSelector);
            return new BattleActionExtraction(reference.FighterId, reference.EffectFile, reference.EffectEntry, scriptSelector, script, null);
        }
        catch (FormatException error)
        {
            return new BattleActionExtraction(reference.FighterId, reference.EffectFile, reference.EffectEntry, scriptSelector, null, error.Message);
        }
    }

    /// <summary>
    /// Extracts the fighter-specific sub-archive from the outer effects archive at the given outer entry index.
    /// </summary>
    /// <param name="archive">Complete outer effects archive bytes.</param>
    /// <param name="effectEntry">Index of the outer archive entry that IS the fighter sub-archive.</param>
    /// <param name="fighterId">Fighter id, used in error messages.</param>
    /// <returns>The raw bytes of the fighter sub-archive.</returns>
    private static byte[] ReadSubArchive(byte[] archive, int effectEntry, int fighterId)
    {
        IReadOnlyList<ArchiveRange> bounds = ModelArchive.ReadBounds(archive);
        if (effectEntry < 0 || effectEntry >= bounds.Count)
        {
            throw new FormatException($"fighter {fighterId} effect archive has {bounds.Count} entries; outer entry {effectEntry} is out of range");
        }

        ArchiveRange range = bounds[effectEntry];
        if (range.IsEmpty)
        {
            throw new FormatException($"fighter {fighterId} outer effect archive entry {effectEntry} is absent");
        }

        return archive[range.Start..range.End];
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
