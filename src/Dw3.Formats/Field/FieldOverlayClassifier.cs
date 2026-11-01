namespace Dw3.Formats.Field;

/// <summary>
/// Classifies a stage overlay as loadable raw MIPS before selector recovery.
/// Raw MIPS instructions are 32-bit words, so a member with trailing bytes cannot
/// be mapped as executable code and is not a field-zone overlay.
/// </summary>
public static class FieldOverlayClassifier
{
    /// <summary>Returns whether the disc member has a whole-word MIPS instruction stream.</summary>
    public static bool IsLoadable(ReadOnlySpan<byte> overlay) => overlay.Length > 0 && overlay.Length % sizeof(uint) == 0;
}
