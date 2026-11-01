namespace Dw3.Formats.Field;

/// <summary>A raw record from the field-stage registry in FIELDSTG.PRO.</summary>
public sealed record FieldStageTableEntry(ushort Mode, int OverlayFileId, uint InitializerAddress);
