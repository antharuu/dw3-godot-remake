namespace Dw3Production.Simulation;

/// <summary>Describes one used equipped form's experience, level, acquired and mastered skills in the victory report.</summary>
/// <param name="Slot">Equipped slot index, 0 to 2.</param>
/// <param name="DigimonId">Persistent form identifier.</param>
/// <param name="ExpBefore">Form experience before settlement.</param>
/// <param name="ExpGained">Form experience received from this battle.</param>
/// <param name="LevelBefore">Form level before settlement.</param>
/// <param name="LevelAfter">Form level after settlement.</param>
/// <param name="AcquiredTechniques">Technique ids newly acquired by this form, in entry order.</param>
/// <param name="MasteredTechniques">Technique ids newly mastered by this form, in entry order.</param>
public sealed record FightReportFormSlot(
    int Slot,
    int DigimonId,
    int ExpBefore,
    int ExpGained,
    int LevelBefore,
    int LevelAfter,
    IReadOnlyList<int> AcquiredTechniques,
    IReadOnlyList<int> MasteredTechniques);
