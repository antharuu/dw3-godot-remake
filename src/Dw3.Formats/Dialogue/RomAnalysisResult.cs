using Dw3.Formats.Field;

namespace Dw3.Formats.Dialogue;

/// <summary>The disc-wide analysis: executable progress metadata and every analysed or failed stage.</summary>
/// <param name="Progress">Progress conditions decoded from the executable.</param>
/// <param name="Stages">Stages whose dialogue setup was fully recovered.</param>
/// <param name="Failures">Stages whose setup cannot be recovered without guessing.</param>
public sealed record RomAnalysisResult(
    ProgressConditionTables Progress,
    IReadOnlyList<StageDialogueAnalysis> Stages,
    IReadOnlyList<StageAnalysisException> Failures);
