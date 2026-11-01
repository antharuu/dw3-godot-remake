namespace Dw3.Formats.Dialogue;

/// <summary>A WSTAG overlay to analyse: its field mode, stage number and raw file bytes.</summary>
/// <param name="Mode">Field mode selecting the overlay.</param>
/// <param name="StageId">Numeric WSTAG identifier.</param>
/// <param name="Data">Raw overlay bytes.</param>
public sealed record StageOverlay(ushort Mode, int StageId, byte[] Data);
