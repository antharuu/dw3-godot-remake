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

    public (int FileNumber, int ArchiveEntry)? GetActorSpriteEntry(int actorId)
    {
        if (actorId < 0 || actorId >= _tables.FileEntries.Count || _tables.FileEntries[actorId] is not NpcFileEntry entry)
            return null;
        return (entry.FileNumber, entry.ArchiveEntry);
    }

    public int? GetActorWidth(int actorId) => actorId >= 0 && actorId < _tables.Widths.Count ? _tables.Widths[actorId] : null;

    // Player-lead classification is not required to decode placement or visibility. It is left false
    // rather than embedding character identifiers that are game content rather than format knowledge.
    public bool IsPlayerLead(int actorId) => false;

    public IReadOnlyList<NpcActorReference> GetStageActors(int stageId) =>
        _actors.TryGetValue(stageId, out IReadOnlyList<NpcActorReference>? actors) ? actors : [];

    public bool IsVisible(IReadOnlyList<NpcCondition> conditions) =>
        NpcSnapshotEvaluator.IsVisible(conditions, SnapshotProgress, _tables.ProgressRanges, _tables.SpecialProgressRanges);

    public string? GetDiscFilePath(int fileNumber) => _filePaths.GetValueOrDefault(fileNumber);
    public int? GetTriggerArchiveEntry(int stageId) => null;
    public IReadOnlyList<int> GetRequiredStageIds() => _stageIds;

    private static DiscFileEntry Require(IReadOnlyList<DiscFileEntry> entries, string path) =>
        entries.SingleOrDefault(entry => string.Equals(entry.IsoPath, path, StringComparison.Ordinal))
        ?? throw new InvalidDataException($"Required disc file is absent: {path}");
}
