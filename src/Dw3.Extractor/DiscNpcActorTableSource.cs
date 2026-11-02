using System.Buffers.Binary;
using Dw3.Disc;
using Dw3.Formats.Field;

namespace Dw3.Extractor;

/// <summary>Disc-backed NPC tables and per-stage actor-list reconstruction.</summary>
internal sealed class DiscNpcActorTableSource : INpcActorTableSource
{
    private const string ExecutablePath = "SLES_039.36";
    private const string FieldStgPath = "AAA/PRO/FIELDSTG.PRO";
    private const int FileEntriesOffset = 0x17150;
    private const int FileEntryCount = 406;
    private const int WidthsOffset = 0x177A8;
    private const int WidthCount = 408;
    private const int FileSectorsOffset = 0x3576C;
    private const int FileSectorCount = 2382;
    private const int ProgressRangesOffset = 0x394B4;
    private const int ProgressRangeCount = 34;
    private const int SpecialConditionsOffset = 0x392D8;
    private const int SpecialConditionsLimit = ProgressRangesOffset;
    private const int SnapshotProgress = 4;
    private const uint OverlayLoadBase = 0x800A5DE0u;
    private const uint FieldStateAddress = 0x80099D80u;

    private readonly NpcDiscTables _tables;
    private readonly IReadOnlyDictionary<int, IReadOnlyList<NpcActorReference>> _actors;
    private readonly IReadOnlyDictionary<int, string> _filePaths;
    private readonly IReadOnlyList<int> _stageIds;

    /// <summary>
    /// Initializes a new instance of the <see cref="DiscNpcActorTableSource"/> class.
    /// All four table fields are assigned directly; use <see cref="Create"/> to build an instance from a live disc.
    /// </summary>
    /// <param name="tables">Decoded NPC disc tables (actor widths, progress conditions, file-sector map).</param>
    /// <param name="actors">Per-stage list of actor references decoded from the stage overlays.</param>
    /// <param name="filePaths">Per-file-number ISO paths used to locate actor sprite archives on the disc.</param>
    /// <param name="stageIds">Ordered list of stage ids whose overlays were successfully decoded.</param>
    private DiscNpcActorTableSource(
        NpcDiscTables tables,
        IReadOnlyDictionary<int, IReadOnlyList<NpcActorReference>> actors,
        IReadOnlyDictionary<int, string> filePaths,
        IReadOnlyList<int> stageIds)
    {
        _tables = tables;
        _actors = actors;
        _filePaths = filePaths;
        _stageIds = stageIds;
    }

    /// <summary>
    /// Reads SLES_039.36 and FIELDSTG.PRO to decode the actor file-entries, actor widths, progress
    /// condition tables, and file-sector map, then scans every WSTAG overlay for actor RAM addresses.
    /// </summary>
    /// <param name="disc">The open disc source to read files from.</param>
    /// <param name="entries">All disc file entries (from disc.ListFiles()).</param>
    /// <returns>A fully populated <see cref="DiscNpcActorTableSource"/> ready for extraction.</returns>
    /// <exception cref="InvalidDataException">A required disc file is absent or the executable table is truncated.</exception>
    public static DiscNpcActorTableSource Create(DiscSource disc, IReadOnlyList<DiscFileEntry> entries)
    {
        byte[] executable = disc.ReadFile(Require(entries, ExecutablePath));
        byte[] fieldStg = disc.ReadFile(Require(entries, FieldStgPath));
        NpcDiscTables tables = NpcDiscTableDecoder.Decode(
            fieldStg, FileEntriesOffset, FileEntryCount, WidthsOffset, WidthCount,
            executable, new NpcProgressTableLocator(ProgressRangesOffset, ProgressRangeCount, SpecialConditionsOffset, SpecialConditionsLimit));

        var lbaToPath = entries
            .GroupBy(entry => entry.Lba)
            .ToDictionary(group => group.Key, group => group.First().IsoPath);
        if (FileSectorsOffset + FileSectorCount * 4 > executable.Length)
            throw new InvalidDataException("Disc executable file-sector table is truncated.");
        var filePaths = new Dictionary<int, string>();
        for (int fileNumber = 0; fileNumber < FileSectorCount; fileNumber++)
        {
            int lba = BinaryPrimitives.ReadInt32LittleEndian(executable.AsSpan(FileSectorsOffset + fileNumber * 4, 4));
            if (lba > 0 && lbaToPath.TryGetValue(lba, out string? path))
                filePaths[fileNumber] = path;
        }

        IReadOnlyList<DiscFieldStage> registry = DiscStageModeReader.BuildRegistry(disc, entries);
        int[] stageIds = [.. registry.Select(stage => stage.StageId).Distinct().Order()];
        var actors = new Dictionary<int, IReadOnlyList<NpcActorReference>>();
        foreach (int stageId in stageIds)
        {
            string path = $"AAA/PRO/WSTAG{stageId}.PRO";
            byte[] overlay = disc.ReadFile(Require(entries, path));
            IReadOnlyList<uint> addresses = NpcStageActorScanner.FindActorAddresses(
                overlay, OverlayLoadBase, FieldStateAddress);
            actors[stageId] = [.. addresses.Select((address, index) => new NpcActorReference(address, index))];
        }
        return new DiscNpcActorTableSource(tables, actors, filePaths, stageIds);
    }

    /// <summary>
    /// Returns the file number and archive entry for the sprite of actor <paramref name="actorId"/>,
    /// or <see langword="null"/> when the id is out of range or the entry is not a file entry.
    /// </summary>
    /// <param name="actorId">The FIELDSTG_fileEntries index; valid range is 0..entryCount-1.</param>
    /// <returns>File number and archive entry, or <see langword="null"/> when unavailable.</returns>
    public (int FileNumber, int ArchiveEntry)? GetActorSpriteEntry(int actorId)
    {
        if (actorId < 0 || actorId >= _tables.FileEntries.Count || _tables.FileEntries[actorId] is not NpcFileEntry entry)
            return null;
        return (entry.FileNumber, entry.ArchiveEntry);
    }

    /// <summary>
    /// Returns the actor width in pixels from FIELDSTG_widths, or <see langword="null"/> when
    /// <paramref name="actorId"/> is out of range.
    /// </summary>
    /// <param name="actorId">The FIELDSTG_widths index; valid range is 0..widthCount-1.</param>
    /// <returns>Width in pixels, or <see langword="null"/> when the id is out of range.</returns>
    public int? GetActorWidth(int actorId) => actorId >= 0 && actorId < _tables.Widths.Count ? _tables.Widths[actorId] : null;

    // Player-lead classification is not required to decode placement or visibility. It is left false
    // rather than embedding character identifiers that are game content rather than format knowledge.

    /// <summary>
    /// Always returns <see langword="false"/>: player-lead classification is not required to decode
    /// placement or visibility and is deferred to avoid embedding game-content character identifiers.
    /// </summary>
    /// <param name="actorId">The actor id to test (unused).</param>
    /// <returns><see langword="false"/>.</returns>
    public bool IsPlayerLead(int actorId) => false;

    /// <summary>
    /// Returns all actor references found in the stage overlay for <paramref name="stageId"/>,
    /// or an empty list when the stage was not in the registry.
    /// </summary>
    /// <param name="stageId">The three-digit stage number.</param>
    /// <returns>An ordered list of actor references, or an empty list.</returns>
    public IReadOnlyList<NpcActorReference> GetStageActors(int stageId) =>
        _actors.TryGetValue(stageId, out IReadOnlyList<NpcActorReference>? actors) ? actors : [];

    /// <summary>
    /// Returns <see langword="true"/> when all NPC conditions are satisfied for the fixed
    /// snapshot progress value (4), using the range and special-progress tables from FIELDSTG.PRO.
    /// </summary>
    /// <param name="conditions">The condition list to evaluate.</param>
    /// <returns><see langword="true"/> when all conditions pass at the snapshot progress value.</returns>
    public bool IsVisible(IReadOnlyList<NpcCondition> conditions) =>
        NpcSnapshotEvaluator.IsVisible(conditions, SnapshotProgress, _tables.ProgressRanges, _tables.SpecialProgressRanges);

    /// <summary>
    /// Returns the ISO path of the archive file for engine file number <paramref name="fileNumber"/>,
    /// or <see langword="null"/> when the number does not map to a disc member.
    /// </summary>
    /// <param name="fileNumber">The engine file number (0-based index into the file-sector table).</param>
    /// <returns>The ISO path, or <see langword="null"/> when not found.</returns>
    public string? GetDiscFilePath(int fileNumber) => _filePaths.GetValueOrDefault(fileNumber);

    /// <summary>
    /// Always returns <see langword="null"/>: trigger archive entries are not resolved from the disc
    /// format in this implementation and are deferred to future work.
    /// </summary>
    /// <param name="stageId">The stage id (unused).</param>
    /// <returns>Always <see langword="null"/>.</returns>
    public int? GetTriggerArchiveEntry(int stageId) => null;

    /// <summary>
    /// Returns the ordered list of stage ids present in the disc's field registry.
    /// </summary>
    /// <returns>Stage ids in ascending numeric order.</returns>
    public IReadOnlyList<int> GetRequiredStageIds() => _stageIds;

    /// <summary>
    /// Returns the first disc entry whose ISO path equals <paramref name="path"/> exactly,
    /// throwing <see cref="InvalidDataException"/> when absent.
    /// </summary>
    /// <param name="entries">The disc file entry list to search.</param>
    /// <param name="path">The ISO path to match (ordinal comparison).</param>
    /// <returns>The matching <see cref="DiscFileEntry"/>.</returns>
    /// <exception cref="InvalidDataException">The required disc file is absent.</exception>
    private static DiscFileEntry Require(IReadOnlyList<DiscFileEntry> entries, string path) =>
        entries.SingleOrDefault(entry => string.Equals(entry.IsoPath, path, StringComparison.Ordinal))
        ?? throw new InvalidDataException($"Required disc file is absent: {path}");
}
