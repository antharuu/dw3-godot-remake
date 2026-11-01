namespace Dw3.Formats.Field;

/// <summary>Stage descriptor members recovered from a WSTAG overlay.</summary>
/// <param name="ObjectsAddress">RAM address of the overlay's StageTile table.</param>
/// <param name="SheetEntry">Archive entry of the stage sprite sheet inside the stage TMPK file.</param>
public sealed record StageDescriptor(uint ObjectsAddress, int SheetEntry);
