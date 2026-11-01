using Dw3.Formats.Field;

namespace Dw3.Extractor;

/// <summary>A field stage classified and decoded entirely from user-disc members.</summary>
public sealed record ExtractedFieldStage(
    int StageId,
    ushort Mode,
    int OverlayFileId,
    string Status,
    string? Error,
    IReadOnlyList<StageSlot> Slots);
