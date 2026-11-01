namespace Dw3.Formats.Field;

/// <summary>One decoded stage slot record (StageSlot, 24-byte disc record).</summary>
/// <param name="Type">Slot type identifier (0 = list terminator).</param>
/// <param name="Arg">Slot-type argument (warp target, climb height, etc.).</param>
/// <param name="X">Map X coordinate of the slot trigger area.</param>
/// <param name="Y">Map Y coordinate of the slot trigger area.</param>
/// <param name="Direction">Facing direction constraint (0..7).</param>
public sealed record NpcStageSlot(ushort Type, ushort Arg, ushort X, ushort Y, ushort Direction);
