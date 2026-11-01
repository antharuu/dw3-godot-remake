using Dw3.Formats.Field;

namespace Dw3.Formats.Dialogue;

/// <summary>
/// Memory layout of the supported disc (SLES-03936) that the stage dialogue analysis relies on.
/// Every value is a location or size in the disc executable or field overlays, never game content.
/// A different disc revision needs its own layout rather than reusing these addresses.
/// </summary>
public sealed record DialogueRomLayout
{
    /// <summary>RAM address of the file-sector table in SLES_039.36: one u32 start LBA per disc file id.</summary>
    public required uint FileSectorsAddress { get; init; }

    /// <summary>Number of entries of the file-sector table.</summary>
    public required int FileSectorCount { get; init; }

    /// <summary>RAM address of the u32 global holding the selected language copy (added to text file ids).</summary>
    public required uint LanguageAddress { get; init; }

    /// <summary>
    /// Number of consecutive disc file ids holding the language copies of one text file. Copy 0 sits at
    /// the id a stage selector stores before the language is added.
    /// </summary>
    public required int LanguageCopiesPerTextFile { get; init; }

    /// <summary>RAM address of the field state record that each stage's setup routine fills in.</summary>
    public required uint FieldStateAddress { get; init; }

    /// <summary>Offset of the u32 text file id member in the field state record.</summary>
    public required short TextFileFieldOffset { get; init; }

    /// <summary>Offset of the actor pointer-list member in the field state record.</summary>
    public required short ActorListFieldOffset { get; init; }

    /// <summary>RAM address at which every WSTAG stage overlay is loaded.</summary>
    public required uint StageOverlayLoadAddress { get; init; }

    /// <summary>RAM address of the progress-range table: <see cref="ProgressRangeCount"/> {u8 min, u8 max} pairs.</summary>
    public required uint ProgressRangesAddress { get; init; }

    /// <summary>Number of progress-range pairs.</summary>
    public required int ProgressRangeCount { get; init; }

    /// <summary>RAM address of the packed 3-byte special-condition records, ended by id 0xFF before the progress ranges.</summary>
    public required uint SpecialConditionsAddress { get; init; }

    /// <summary>File offset in FIELDSTG.PRO of the u8 talk/collision width table, indexed by character id.</summary>
    public required int ActorWidthsOffset { get; init; }

    /// <summary>Number of entries of the width table.</summary>
    public required int ActorWidthCount { get; init; }

    /// <summary>The European disc SLES-03936.</summary>
    public static DialogueRomLayout Sles03936 { get; } = new()
    {
        FileSectorsAddress = 0x80044F6C,
        FileSectorCount = 2382,
        LanguageAddress = 0x8005CCA8,
        LanguageCopiesPerTextFile = 7,
        FieldStateAddress = 0x80099D80,
        TextFileFieldOffset = 0x44,
        ActorListFieldOffset = NpcStageActorScanner.ActorListFieldOffset,
        StageOverlayLoadAddress = FieldOverlayLayout.LoadAddress,
        ProgressRangesAddress = 0x80048CB4,
        ProgressRangeCount = 34,
        SpecialConditionsAddress = 0x80048AD8,
        ActorWidthsOffset = 0x177A8,
        ActorWidthCount = 408,
    };

    /// <summary>Reads the u8 width table from the FIELDSTG.PRO bytes.</summary>
    /// <exception cref="FormatException">The table is outside the file.</exception>
    public IReadOnlyList<int> ReadActorWidths(ReadOnlySpan<byte> fieldStg)
    {
        if (ActorWidthsOffset < 0 || (long)ActorWidthsOffset + ActorWidthCount > fieldStg.Length)
            throw new FormatException($"Actor width table 0x{ActorWidthsOffset:X}+{ActorWidthCount} is outside FIELDSTG.PRO ({fieldStg.Length} bytes).");
        var widths = new int[ActorWidthCount];
        for (int index = 0; index < widths.Length; index++)
            widths[index] = fieldStg[ActorWidthsOffset + index];
        return widths;
    }
}
