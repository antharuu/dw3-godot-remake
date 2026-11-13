namespace Dw3Production.Simulation;

/// <summary>Describes one used permanent form's experience, level and newly learned techniques in the victory report.</summary>
/// <param name="DigimonId">Persistent form identifier.</param>
/// <param name="ExpBefore">Form experience before settlement.</param>
/// <param name="ExpGained">Form experience received from this battle.</param>
/// <param name="LevelBefore">Form level before settlement.</param>
/// <param name="LevelAfter">Form level after settlement.</param>
/// <param name="LearnedTechniques">Technique ids newly learned by this form.</param>
public sealed record FightReportFormSlot(
    int DigimonId,
    int ExpBefore,
    int ExpGained,
    int LevelBefore,
    int LevelAfter,
    IReadOnlyList<int> LearnedTechniques);
