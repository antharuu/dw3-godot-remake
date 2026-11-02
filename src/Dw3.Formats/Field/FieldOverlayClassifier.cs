namespace Dw3.Formats.Field;

/// <summary>
/// Classifies a stage overlay as loadable raw MIPS before selector recovery.
/// Raw MIPS instructions are 32-bit words, so a member with trailing bytes cannot
/// be mapped as executable code and is not a field-zone overlay.
/// </summary>
public static class FieldOverlayClassifier
{
    /// <summary>Returns whether the disc member has a whole-word MIPS instruction stream.</summary>
    /// <param name="overlay">Raw bytes of the disc member to classify.</param>
    /// <returns><see langword="true"/> when the member is non-empty and its length is a multiple of 4 bytes.</returns>
    public static bool IsLoadable(ReadOnlySpan<byte> overlay) => overlay.Length > 0 && overlay.Length % sizeof(uint) == 0;
}
