using Dw3Production.Simulation;

namespace Dw3Production.Session;
/// <summary>Captures a form entry before settlement so the report can show its exact experience and learning delta.</summary>
/// <param name="FormId">Persistent form identifier.</param>
/// <param name="Exp">Experience before settlement.</param>
/// <param name="Level">Level before settlement.</param>
/// <param name="Techniques">Technique ids known before settlement.</param>
internal sealed record FormSnapshot(int FormId, int Exp, int Level, IReadOnlyList<int> Techniques)
{
    /// <summary>Captures one mutable persistent form entry.</summary>
    /// <param name="progress">Form entry before progression.</param>
    /// <returns>An immutable report snapshot.</returns>
    public static FormSnapshot Capture(PartnerFormProgress progress) => new(progress.FormId, progress.Exp, progress.Level, [.. progress.Techniques]);

    /// <summary>Maps the saved before-state and advanced entry to a form report row.</summary>
    /// <param name="after">Form entry after progression.</param>
    /// <returns>The matching report row.</returns>
    public FightReportFormSlot ToReport(PartnerFormProgress after) => new(FormId, Exp, after.Exp - Exp, Level, after.Level, [.. after.Techniques.Except(Techniques)]);
}
