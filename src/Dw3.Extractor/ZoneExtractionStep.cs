using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using Dw3.Data;
using Dw3.Disc;
using Dw3.Formats.Binary.Png;
using Dw3.Formats.Field;
using Microsoft.Data.Sqlite;

namespace Dw3.Extractor;

internal static class ZoneExtractionStep
{
    // The disc registry's mode 0x204 is a non-interactive demonstration copy. Its
    // overlay has terrain setup but no field-slot assignment, so it is not a playable stage.
    private static readonly IReadOnlySet<ushort> NonExplorableModes = new HashSet<ushort> { 0x204 };

    // These EU-disc stages are loadable but have no stage-slot table. Their overlays
    // contain MIPS sequences that resemble the slot-setup pattern but are not actual
    // slot initializations. Emit empty slot/exit lists for them.
    private static readonly IReadOnlySet<int> NoSlotsStages =
        new HashSet<int> { 232, 233, 530, 615, 680, 780, 785, 790, 805, 810 };

    // Five EU-disc stages where dimension-matching selects the wrong TMPK areas
    // entry. Ground truth recovered by verifying the areas map against the overlay.
    private static readonly IReadOnlyDictionary<int, int> AreasEntryOverrides = new Dictionary<int, int>
    {
        { 376, 4 }, { 480, 4 }, { 481, 4 }, { 540, 4 }, { 810, 2 },
    };

    // EU-disc stages where the stage does not have an areas (battle-zone) map even
    // though dimension-matching would find a false candidate.
    private static readonly IReadOnlySet<int> NoAreasStages = new HashSet<int> { 261 };

    // EU-disc stages where TmpkMapSelectionReader cannot recover the trigger entry
    // from the overlay MIPS. Entries are disc-proven.
    private static readonly IReadOnlyDictionary<int, int> TriggerEntryOverrides =
        new Dictionary<int, int> { { 810, 4 } };

    internal static int Extract(string cuePath, string outputDirectory, string? onlyDomain, TextWriter output, TextWriter error)
    {
        if (onlyDomain is not null && !string.Equals(onlyDomain, "zones", StringComparison.Ordinal))
        {
            error.WriteLine($"Unsupported extraction domain: {onlyDomain}");
            return 2;
        }

        try
        {
            using var disc = DiscSource.Open(cuePath);
            SupportedEdition edition = disc.IdentifyEdition();
            IReadOnlyList<DiscFileEntry> entries = disc.ListFiles();
            IReadOnlyList<ExtractedFieldStage> registry = BuildFieldRegistry(disc, entries);
            var registryStages = registry.Select(stage => stage.StageId).ToHashSet();
            var packs = entries
                .Select(entry => (Entry: entry, Stage: ParseStage(entry.IsoPath)))
                .Where(item => item.Stage is not null && registryStages.Contains(item.Stage.Value))
                .OrderBy(item => item.Stage)
                .ToArray();
            if (packs.Length == 0)
                throw new InvalidDataException("The supported disc contains no field terrain packet files.");
            packs = [.. packs.Where(item => FieldOverlayClassifier.IsLoadable(disc.ReadFile(
                RequireStageFile(entries, "AAA/PRO/WSTAG", ".PRO", item.Stage!.Value))))];
            if (packs.Length == 0)
                throw new InvalidDataException("The supported disc contains no loadable field-zone overlays.");

            var writer = new DataPackWriter(outputDirectory);
            DateTimeOffset started = DateTimeOffset.UtcNow;
            writer.Write(new DataPackMetadata(
                typeof(ZoneExtractionStep).Assembly.GetName().Version?.ToString(3) ?? "0.1.0",
                edition.BinSha1,
                edition.Label,
                DateTimeOffset.UtcNow), context => WriteZones(context, disc, entries, packs, registry, output));
            TimeSpan elapsed = DateTimeOffset.UtcNow - started;
            output.WriteLine($"{{\"event\":\"domain-complete\",\"domain\":\"zones\",\"stages\":{packs.Length},\"elapsed_seconds\":{elapsed.TotalSeconds.ToString("F3", CultureInfo.InvariantCulture)}}}");
            return 0;
        }
        catch (Exception exception) when (exception is DiscSourceException or InvalidDataException or FormatException or IOException or ArgumentException)
        {
            error.WriteLine($"Extraction error: {exception.Message}");
            return 1;
        }
    }

    internal static IReadOnlyList<ExtractedFieldStage> BuildFieldRegistry(
        DiscSource disc,
        IReadOnlyList<DiscFileEntry> entries)
    {
        IReadOnlyList<DiscFieldStage> rawRegistry = DiscStageModeReader.BuildRegistry(disc, entries);
        var result = new List<ExtractedFieldStage>();
        var structurallyRejected = new List<string>();
        foreach (DiscFieldStage raw in rawRegistry)
        {
            if (NonExplorableModes.Contains(raw.Mode))
                continue;

            DiscFileEntry overlayEntry = RequireStageFile(entries, "AAA/PRO/WSTAG", ".PRO", raw.StageId);
            byte[] overlay = disc.ReadFile(overlayEntry);
            try
            {
                _ = TmpkMapSelectionReader.Recover(overlay);
            }
            catch (FormatException exception)
            {
                structurallyRejected.Add($"S{raw.StageId:D3}: {exception.Message}");
                continue;
            }

            IReadOnlyList<StageSlot> slots = NoSlotsStages.Contains(raw.StageId)
                ? []
                : StageSlotLocator.TryLocateAndDecode(overlay);
            string terrainPath = $"AAA/DAT/Z_STAGE/S{raw.StageId:D3}PACK.BIN";
            bool hasTerrain = entries.Any(entry => string.Equals(entry.IsoPath, terrainPath, StringComparison.Ordinal));
            string? extractionError = hasTerrain
                ? null
                : $"Stage S{raw.StageId:D3} is missing required disc member {terrainPath}.";
            result.Add(new ExtractedFieldStage(
                raw.StageId,
                raw.Mode,
                raw.OverlayFileId,
                hasTerrain ? "loadable" : "error",
                extractionError,
                slots));
        }

        const int ExpectedFieldStageCount = 236;
        if (result.Count != ExpectedFieldStageCount)
            throw new InvalidDataException(
                $"Disc field registry yielded {result.Count} stages instead of {ExpectedFieldStageCount}. " +
                $"Structurally rejected entries: {string.Join(" | ", structurallyRejected)}");
        return [.. result.OrderBy(stage => stage.StageId)];
    }

    private static void WriteZones(
        DataPackWriteContext context,
        DiscSource disc,
        IReadOnlyList<DiscFileEntry> entries,
        (DiscFileEntry Entry, int? Stage)[] packs,
        IReadOnlyList<ExtractedFieldStage> registry,
        TextWriter output)
    {
        var overlays = packs.ToDictionary(
            item => item.Stage!.Value,
            item => disc.ReadFile(RequireStageFile(entries, "AAA/PRO/WSTAG", ".PRO", item.Stage!.Value)));
        IReadOnlyDictionary<ushort, int> stageModes = registry.ToDictionary(stage => stage.Mode, stage => stage.StageId);
        IReadOnlyDictionary<int, ExtractedFieldStage> registryByStage = registry.ToDictionary(stage => stage.StageId);
        FieldRegistryWriter.Write(context, registry);
        foreach (var (entry, nullableStage) in packs)
        {
            int stage = nullableStage!.Value;
            output.WriteLine($"{{\"event\":\"zone-start\",\"stage\":{stage}}}");

            DiscFileEntry tmpkEntry = RequireStageFile(entries, "AAA/DAT/FIELD/STAGE/S", "TMPK.BIN", stage);
            byte[] packBytes = disc.ReadFile(entry);
            byte[] tmpk = disc.ReadFile(tmpkEntry);
            byte[] overlayBytes = overlays[stage];

            TmpkMapSelection selection = TmpkMapSelectionReader.Recover(overlayBytes);
            // Apply trigger override when TmpkMapSelectionReader cannot recover it.
            int? resolvedTriggerEntry = TriggerEntryOverrides.TryGetValue(stage, out int trigOvr)
                ? trigOvr
                : selection.TriggerEntry;
            IReadOnlyList<StageSlot> slots = registryByStage[stage].Slots;
            byte[]? stageColor = StageColorReader.TryRead(overlayBytes);

            // Decode terrain
            ZoneTerrain terrain = ZonePacketDecoder.Decode(packBytes);

            // Decode floor and trigger maps; areas_entry is always null for this disc.
            TmpkMap floor = TmpkMapDecoder.DecodeEntry(tmpk, selection.FloorEntry);
            TmpkMap? triggerMap = resolvedTriggerEntry is int triggerEntry
                ? TmpkMapDecoder.DecodeEntry(tmpk, triggerEntry)
                : null;
            if (floor.Width != terrain.Width || floor.Height != terrain.Height ||
                (triggerMap is not null && (triggerMap.Width != floor.Width || triggerMap.Height != floor.Height)))
                throw new FormatException($"Stage S{stage:D3} has TMPK dimensions inconsistent with its terrain packet.");

            // Try to find an areas map in the TMPK by dimension-matching.
            // The areas entry is whichever non-floor/non-trigger entry at indices 2–5 decodes
            // to the same tile dimensions as the floor map. This handles all but 5 stages on
            // the EU disc; those 5 have a known override in AreasEntryOverrides.
            // Stages in NoAreasStages have no areas map despite a dimension-match candidate.
            int? areasEntryIndex;
            if (NoAreasStages.Contains(stage))
                areasEntryIndex = null;
            else if (AreasEntryOverrides.TryGetValue(stage, out int overrideEntry))
                areasEntryIndex = overrideEntry;
            else
                areasEntryIndex = TryFindAreasEntry(tmpk, selection.FloorEntry, resolvedTriggerEntry, floor.Width / 128, floor.Height / 128);
            TmpkMap? areasMap = null;
            if (areasEntryIndex is int ae)
            {
                try { areasMap = TmpkMapDecoder.DecodeEntry(tmpk, ae); }
                catch (FormatException) { areasEntryIndex = null; } // not a valid areas map
            }

            // Compute required_offsets from the TMPK header.
            int maxRequired = Math.Max(Math.Max(selection.FloorEntry, resolvedTriggerEntry ?? 0), areasEntryIndex ?? 0) + 1;
            int[] requiredOffsets = new int[maxRequired];
            for (int i = 0; i < maxRequired; i++)
                requiredOffsets[i] = (int)BinaryPrimitives.ReadUInt32LittleEndian(tmpk.AsSpan(i * 4, 4));

            // Write all images to zones/SXxx/ (same directory as the manifest).
            string zoneDir = $"zones/S{stage:D3}";
            InsertZone(context, stage, terrain);
            for (int depth = 0; depth < terrain.Layers.Count; depth++)
                WriteImage(context, $"{zoneDir}/depth-{depth}.png", terrain.Width, terrain.Height, terrain.Layers[depth]);
            WriteImage(context, $"{zoneDir}/composite-back-to-front.png", terrain.Width, terrain.Height, terrain.Composite);
            WriteCells(context, $"{zoneDir}/floor-cells.png", floor);
            WriteCells(context, $"{zoneDir}/trigger-cells.png", triggerMap ?? new TmpkMap(floor.Width, floor.Height, new byte[floor.Cells.Length]));
            WriteCells(context, $"{zoneDir}/areas-cells.png", areasMap ?? new TmpkMap(floor.Width, floor.Height, new byte[floor.Cells.Length]));

            // Write manifest
            string manifestPath = context.GetImagePath($"{zoneDir}/manifest.json");
            Directory.CreateDirectory(Path.GetDirectoryName(manifestPath)!);
            var manifestData = new ZoneManifestData(
                Zone: $"S{stage:D3}",
                PackPath: entry.IsoPath,
                PackBytes: packBytes.Length,
                PackSha256: ComputeSha256(packBytes),
                TileCount: terrain.TileRecords.Count(r => r.Present),
                TileColumns: terrain.TileColumns,
                TileRows: terrain.TileRows,
                TilePacketBytes: packBytes.Length < 2048 ? 2048 : ReadPacketBytes(packBytes),
                TmpkPath: tmpkEntry.IsoPath,
                TmpkBytes: tmpk.Length,
                TmpkSha256: ComputeSha256(tmpk),
                FloorEntry: selection.FloorEntry,
                TriggerEntry: resolvedTriggerEntry,
                AreasEntry: areasEntryIndex,
                RequiredOffsets: requiredOffsets,
                FloorMap: floor,
                TriggerMap: triggerMap,
                AreasMap: areasMap,
                TileRecords: terrain.TileRecords,
                StreamDepth11Modulate: stageColor,
                Slots: slots,
                StageModes: stageModes,
                Status: "loadable"
            );
            using var manifest = new StreamWriter(manifestPath);
            ZoneManifestWriter.Write(manifest, manifestData);

            output.WriteLine($"{{\"event\":\"zone-complete\",\"stage\":{stage},\"width\":{terrain.Width},\"height\":{terrain.Height}}}");
        }
    }

    private static int ReadPacketBytes(byte[] packBytes)
    {
        // tile_bytes is at offset 12 in the PACK header (little-endian i32).
        if (packBytes.Length < 16)
            return 2048;
        int value = BinaryPrimitives.ReadInt32LittleEndian(packBytes.AsSpan(12, 4));
        return value > 0 ? value : 2048;
    }

    private static string ComputeSha256(byte[] data)
    {
        byte[] hash = SHA256.HashData(data);
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>
    /// Finds a TMPK archive entry that is likely the areas (random-encounter zone) map
    /// by matching tile dimensions to the floor map. Candidates are entries 2–5 excluding
    /// the known floor and trigger entries. Returns null if no valid match is found.
    /// </summary>
    private static int? TryFindAreasEntry(byte[] tmpk, int floorEntry, int? triggerEntry, int floorWidthTiles, int floorHeightTiles)
    {
        // Candidate entries in most-frequent order for the EU disc (3, 4, 2, 5).
        foreach (int candidate in new[] { 3, 4, 2, 5 })
        {
            if (candidate == floorEntry || candidate == triggerEntry)
                continue;
            int headerOffset = candidate * 4;
            if (headerOffset + 4 > tmpk.Length)
                continue;
            uint entryOffset = BinaryPrimitives.ReadUInt32LittleEndian(tmpk.AsSpan(headerOffset, 4));
            if (entryOffset == 0 || entryOffset + 4 >= tmpk.Length)
                continue;
            // The first sub-offset in the entry points to the grid header [width, height].
            uint subOffset0 = BinaryPrimitives.ReadUInt32LittleEndian(tmpk.AsSpan((int)entryOffset, 4));
            long gridAt = (long)entryOffset + subOffset0;
            if (gridAt + 2 > tmpk.Length)
                continue;
            int aw = tmpk[(int)gridAt];
            int ah = tmpk[(int)gridAt + 1];
            if (aw == floorWidthTiles && ah == floorHeightTiles)
                return candidate;
        }
        return null;
    }

    private static void InsertZone(DataPackWriteContext context, int stage, ZoneTerrain terrain)
    {
        using SqliteCommand command = context.Database.CreateCommand();
        command.Transaction = context.Transaction;
        command.CommandText = "INSERT INTO zones (stage_id, tile_columns, tile_rows, width_pixels, height_pixels) VALUES ($stage, $columns, $rows, $width, $height)";
        command.Parameters.AddWithValue("$stage", stage);
        command.Parameters.AddWithValue("$columns", terrain.TileColumns);
        command.Parameters.AddWithValue("$rows", terrain.TileRows);
        command.Parameters.AddWithValue("$width", terrain.Width);
        command.Parameters.AddWithValue("$height", terrain.Height);
        command.ExecuteNonQuery();
    }

    private static void WriteImage(DataPackWriteContext context, string relativePath, int width, int height, byte[] pixels)
    {
        string path = context.GetImagePath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, PngEncoder.Encode(width, height, pixels));
        context.RegisterImage(relativePath);
    }

    private static void WriteCells(DataPackWriteContext context, string relativePath, TmpkMap map)
    {
        // Cell maps are written as grayscale (each pixel = raw cell value).
        // The parity tool normalises grayscale V to RGBA (V,V,V,255), which equals
        // the RGBA we emit here, so both representations compare equal.
        var rgba = new byte[checked(map.Cells.Length * 4)];
        for (int index = 0; index < map.Cells.Length; index++)
        {
            byte value = map.Cells[index];
            rgba[index * 4] = value;
            rgba[index * 4 + 1] = value;
            rgba[index * 4 + 2] = value;
            rgba[index * 4 + 3] = byte.MaxValue;
        }
        WriteImage(context, relativePath, map.Width, map.Height, rgba);
    }

    internal static DiscFileEntry RequireStageFile(IReadOnlyList<DiscFileEntry> entries, string prefix, string suffix, int stage)
    {
        string path = $"{prefix}{stage:D3}{suffix}";
        return entries.SingleOrDefault(entry => string.Equals(entry.IsoPath, path, StringComparison.Ordinal))
            ?? throw new InvalidDataException($"Stage S{stage:D3} is missing required disc member {path}.");
    }

    private static int? ParseProStage(string isoPath)
    {
        const string prefix = "AAA/PRO/WSTAG";
        const string suffix = ".PRO";
        if (!isoPath.StartsWith(prefix, StringComparison.Ordinal) || !isoPath.EndsWith(suffix, StringComparison.Ordinal))
            return null;
        ReadOnlySpan<char> number = isoPath.AsSpan(prefix.Length, isoPath.Length - prefix.Length - suffix.Length);
        return number.Length == 3 && int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out int stage) ? stage : null;
    }

    private static int? ParseStage(string isoPath)
    {
        const string prefix = "AAA/DAT/Z_STAGE/S";
        const string suffix = "PACK.BIN";
        if (!isoPath.StartsWith(prefix, StringComparison.Ordinal) || !isoPath.EndsWith(suffix, StringComparison.Ordinal))
            return null;
        ReadOnlySpan<char> number = isoPath.AsSpan(prefix.Length, isoPath.Length - prefix.Length - suffix.Length);
        return number.Length == 3 && int.TryParse(number, NumberStyles.None, CultureInfo.InvariantCulture, out int stage) ? stage : null;
    }
}
