using System.Buffers.Binary;
using Dw3.Disc;
using Dw3.Formats.Field;

namespace Dw3.Extractor;

/// <summary>
/// Reads the FIELDSTG_stages table from the FIELDSTG.PRO overlay file and the FILE_SECTORS
/// lookup table from SLES_039.36, then builds a complete mode-to-stage-number map for the EU disc.
/// </summary>
/// <remarks>
/// The FIELDSTG_stages table is a runtime-populated array in FIELDSTG.PRO that maps each
/// field mode to its overlay file index. FILE_SECTORS in SLES_039.36 maps overlay file indices
/// to disc sector addresses (LBAs), which are cross-referenced against the ISO directory to
/// recover stage numbers from WSTAG filenames.
///
/// Disc constants are derived from the EU disc (SLES_039.36) by binary correlation:
///   - FIELDSTG.PRO virtual load address: 0x80082CB0
///   - FIELDSTG_stages RAM address:       0x8009A884
///   - FILE_SECTORS base offset verified against WSTAG200.PRO LBA at index 466
/// </remarks>
public static class DiscStageModeReader
{
    // ── Disc constants (EU BIN/CUE, SLES_039.36 / FIELDSTG.PRO) ──────────

    /// <summary>Virtual load address of the FIELDSTG.PRO overlay in the EU disc.</summary>
    private const uint FieldStgLoadAddress = 0x80082CB0u;

    /// <summary>RAM address of the FIELDSTG_stages array when the overlay is loaded.</summary>
    private const uint FieldStgStagesRamAddress = 0x8009A884u;

    /// <summary>Number of entries in the FIELDSTG_stages array on the EU disc.</summary>
    private const int FieldStgStagesEntryCount = 240;

    /// <summary>
    /// Byte offset of the FILE_SECTORS array within SLES_039.36.
    /// Verified at runtime: FILE_SECTORS[466] must match the LBA of WSTAG200.PRO.
    /// </summary>
    private const int FileSectorsBaseOffset = 0x3576C;

    /// <summary>Known-good verification index: overlay index 466 → WSTAG200.PRO.</summary>
    private const int VerificationOverlayIndex = 466;

    /// <summary>ISO path of the SLES_039.36 executable on the disc.</summary>
    private const string SlesIsoPath = "SLES_039.36";

    /// <summary>ISO path of the FIELDSTG overlay on the disc.</summary>
    private const string FieldStgIsoPath = "AAA/PRO/FIELDSTG.PRO";

    // ── Entry layout in FIELDSTG_stages ──────────────────────────────────

    private const int FieldStgStagesFileOffset = (int)(FieldStgStagesRamAddress - FieldStgLoadAddress);

    // ── Public API ────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a complete map from field mode to stage number by reading the disc.
    /// </summary>
    /// <param name="disc">Open disc source to read files from.</param>
    /// <param name="entries">All file entries on the disc (from disc.ListFiles()).</param>
    /// <returns>Dictionary mapping each 16-bit field mode to its stage number.</returns>
    /// <exception cref="InvalidDataException">Thrown when required disc files are absent or malformed.</exception>
    public static IReadOnlyDictionary<ushort, int> Build(DiscSource disc, IReadOnlyList<DiscFileEntry> entries) =>
        BuildRegistry(disc, entries).ToDictionary(stage => stage.Mode, stage => stage.StageId);

    /// <summary>
    /// Reads the complete raw registry and resolves each overlay file id to its WSTAG disc member.
    /// </summary>
    public static IReadOnlyList<DiscFieldStage> BuildRegistry(DiscSource disc, IReadOnlyList<DiscFileEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(disc);
        ArgumentNullException.ThrowIfNull(entries);

        var lbaToStageName = BuildLbaToStageNameMap(entries);
        byte[] slesData = ReadRequiredFile(disc, entries, SlesIsoPath);
        byte[] fieldStgData = ReadRequiredFile(disc, entries, FieldStgIsoPath);
        IReadOnlyList<int> fileSectors = ReadFileSectors(slesData, lbaToStageName, entries);
        return ParseStageRegistry(fieldStgData, fileSectors, lbaToStageName);
    }

    // ── Implementation ────────────────────────────────────────────────────

    private static Dictionary<int, string> BuildLbaToStageNameMap(IReadOnlyList<DiscFileEntry> entries)
    {
        var map = new Dictionary<int, string>();
        foreach (var entry in entries)
        {
            string name = Path.GetFileName(entry.IsoPath);
            // Match WSTAG###.PRO (three digits, uppercase).
            if (name.StartsWith("WSTAG", StringComparison.Ordinal) &&
                name.EndsWith(".PRO", StringComparison.Ordinal) &&
                name.Length == "WSTAG000.PRO".Length)
            {
                map[entry.Lba] = name;
            }
        }
        return map;
    }

    private static int[] ReadFileSectors(byte[] slesData, Dictionary<int, string> lbaToStageName,
        IReadOnlyList<DiscFileEntry> entries)
    {
        _ = lbaToStageName;
        // The FILE_SECTORS array is a flat int32[] at a known offset in SLES_039.36.
        // We verify by checking that FILE_SECTORS[466] equals the LBA of WSTAG200.PRO.
        int verificationLba = entries
            .FirstOrDefault(e => string.Equals(e.IsoPath, "AAA/PRO/WSTAG200.PRO", StringComparison.Ordinal))
            ?.Lba ?? 0;
        if (verificationLba == 0)
            throw new InvalidDataException("WSTAG200.PRO is absent from the disc; cannot verify FILE_SECTORS base.");

        int verificationOffset = FileSectorsBaseOffset + VerificationOverlayIndex * sizeof(int);
        if (verificationOffset + sizeof(int) > slesData.Length)
            throw new InvalidDataException($"FILE_SECTORS verification offset 0x{verificationOffset:X} is out of {SlesIsoPath} range.");

        int storedLba = BinaryPrimitives.ReadInt32LittleEndian(slesData.AsSpan(verificationOffset, sizeof(int)));
        if (storedLba != verificationLba)
            throw new InvalidDataException(
                $"FILE_SECTORS[{VerificationOverlayIndex}] = {storedLba} but WSTAG200.PRO LBA = {verificationLba}; disc or offset mismatch.");

        // The array length is determined by available data; cap at a safe maximum.
        int maxEntries = (slesData.Length - FileSectorsBaseOffset) / sizeof(int);
        var result = new int[maxEntries];
        for (int i = 0; i < maxEntries; i++)
            result[i] = BinaryPrimitives.ReadInt32LittleEndian(slesData.AsSpan(FileSectorsBaseOffset + i * sizeof(int), sizeof(int)));
        return result;
    }

    /// <summary>Exposed as internal for unit-testing with synthetic data.</summary>
    internal static IReadOnlyDictionary<ushort, int> ParseStageModes(byte[] fieldStgData,
        IReadOnlyList<int> fileSectors, Dictionary<int, string> lbaToStageName,
        int entryCount = FieldStgStagesEntryCount, int tableOffset = FieldStgStagesFileOffset) =>
        ParseStageRegistry(fieldStgData, fileSectors, lbaToStageName, entryCount, tableOffset)
            .ToDictionary(stage => stage.Mode, stage => stage.StageId);

    internal static IReadOnlyList<DiscFieldStage> ParseStageRegistry(byte[] fieldStgData,
        IReadOnlyList<int> fileSectors, Dictionary<int, string> lbaToStageName,
        int entryCount = FieldStgStagesEntryCount, int tableOffset = FieldStgStagesFileOffset)
    {
        IReadOnlyList<FieldStageTableEntry> rawEntries;
        try
        {
            rawEntries = FieldStageTableDecoder.Decode(fieldStgData, tableOffset, entryCount);
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException($"Invalid FIELDSTG stage registry: {exception.Message}", exception);
        }

        var result = new List<DiscFieldStage>();
        foreach (FieldStageTableEntry raw in rawEntries)
        {
            if (raw.OverlayFileId >= fileSectors.Count)
                continue;
            int lba = fileSectors[raw.OverlayFileId];
            if (lba <= 0 || !lbaToStageName.TryGetValue(lba, out string? stageName))
                continue;
            if (!stageName.StartsWith("WSTAG", StringComparison.Ordinal) ||
                !stageName.EndsWith(".PRO", StringComparison.Ordinal) ||
                stageName.Length != "WSTAG000.PRO".Length)
                continue;

            ReadOnlySpan<char> digits = stageName.AsSpan("WSTAG".Length, 3);
            if (!int.TryParse(digits, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out int stageNumber))
                throw new InvalidDataException($"Malformed field overlay filename in disc registry: {stageName}");
            result.Add(new DiscFieldStage(raw.Mode, stageNumber, raw.OverlayFileId, raw.InitializerAddress));
        }
        return result;
    }

    private static byte[] ReadRequiredFile(DiscSource disc, IReadOnlyList<DiscFileEntry> entries, string isoPath)
    {
        DiscFileEntry? entry = entries.SingleOrDefault(e =>
            string.Equals(e.IsoPath, isoPath, StringComparison.Ordinal)) ?? throw new InvalidDataException($"Required disc file is absent: {isoPath}");
        return disc.ReadFile(entry);
    }
}
