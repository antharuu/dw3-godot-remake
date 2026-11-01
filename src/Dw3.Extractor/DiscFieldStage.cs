namespace Dw3.Extractor;

/// <summary>A field-stage registry record resolved to a WSTAG member on the user disc.</summary>
public sealed record DiscFieldStage(ushort Mode, int StageId, int OverlayFileId, uint InitializerAddress);
