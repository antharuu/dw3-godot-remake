namespace Dw3Production.Conversion;

/// <summary>Defines source-confirmed absences in the supported European effect-model action population.</summary>
internal static class SupportedEffectModelAbsence
{
    private const int EffectModelId = 373;

    /// <summary>Returns whether a missing prepared row is a source-confirmed absence rather than an incomplete pack.</summary>
    /// <param name="effectId">Script-visible effect-model id missing from the prepared registry.</param>
    /// <returns><see langword="true"/> only for the source-confirmed absence.</returns>
    internal static bool IsQualified(int effectId) => effectId == EffectModelId;
}
