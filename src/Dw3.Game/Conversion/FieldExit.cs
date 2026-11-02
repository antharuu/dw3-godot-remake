namespace Dw3Production.Conversion;

/// <summary>A disc-decoded SLOT_EXIT (Type 0 or 1): the destination zone, spawn position and
/// optional story condition that gates the transition until progress reaches the required state.</summary>
public sealed record FieldExit(int Slot, string Destination, int X, int Y, int Facing, int? Condition = null, string ConditionStatus = "unconditional")
{
    /// <summary>Gets a value indicating whether this physical edge is story-gated and opened for the exploration
    /// phase without evaluating its condition. The raw condition stays on <see cref="Condition"/>.</summary>
    public bool IsUnevaluated => ConditionStatus == "unevaluated";
}
