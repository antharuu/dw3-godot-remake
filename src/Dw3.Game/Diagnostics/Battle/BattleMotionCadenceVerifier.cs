using System.Text.Json;
using Dw3Production.Diagnostics.Field;
using Dw3Production.Logging;
using Dw3Production.Presentation;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Compares and persists end-to-end production motion-cadence evidence.</summary>
internal static class BattleMotionCadenceVerifier
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    /// <summary>Requires every real actor's equal-time trace to match the 60 FPS reference and cover the requested state transitions.</summary>
    /// <param name="trials">Cadence-indexed actor traces from the production renderer.</param>
    /// <param name="cadences">All tested presentation cadences.</param>
    /// <param name="fighters">Real priority actors captured in each trial.</param>
    public static void AssertEquivalent(
        IReadOnlyDictionary<int, IReadOnlyDictionary<int, BattleMotionCadenceTrace>> trials,
        IReadOnlyCollection<int> cadences,
        IReadOnlyCollection<int> fighters)
    {
        var reference = trials[60];
        foreach (var cadence in cadences.Where(cadence => cadence != 60))
        {
            AssertActorsEqual(reference, trials[cadence], cadence, fighters);
        }

        foreach (var fighter in fighters)
        {
            AssertRequiredTransitions(reference[fighter].Samples, fighter);
        }
    }

    /// <summary>Writes every compared publication and effective bone transform outside Git for later inspection.</summary>
    /// <param name="captureRoot">Private probe output directory.</param>
    /// <param name="trials">Cadence-indexed actor traces that passed comparison.</param>
    public static void WriteEvidence(string captureRoot, IReadOnlyDictionary<int, IReadOnlyDictionary<int, BattleMotionCadenceTrace>> trials)
    {
        var path = Path.Combine(captureRoot, "cadence-equivalence.json");
        File.WriteAllText(path, JsonSerializer.Serialize(trials, JsonOptions));
        QuietLog.Info($"battle-motion cadence PASS rates=30,60,120 actors=383,51 samples=8 evidence={path}");
    }

    /// <summary>Requires the candidate cadence to retain every published state and bone pose from the reference.</summary>
    /// <param name="reference">60 FPS actor traces.</param>
    /// <param name="candidate">Candidate cadence actor traces.</param>
    /// <param name="cadence">Candidate presentation frame rate.</param>
    /// <param name="fighters">Real actors being compared.</param>
    private static void AssertActorsEqual(
        IReadOnlyDictionary<int, BattleMotionCadenceTrace> reference,
        IReadOnlyDictionary<int, BattleMotionCadenceTrace> candidate,
        int cadence,
        IReadOnlyCollection<int> fighters)
    {
        foreach (var fighter in fighters)
        {
            var difference = BattleMotionCadenceTrace.Compare(reference[fighter], candidate[fighter]);
            ProbeRequirement.Require(difference is null, $"cadence={cadence} fighter={fighter} differs={difference}");
        }
    }

    /// <summary>Requires an interrupted blend path and a completed return to source idle.</summary>
    /// <param name="trace">60 FPS actor checkpoints.</param>
    /// <param name="fighter">Source fighter identifier used in diagnostics.</param>
    private static void AssertRequiredTransitions(IReadOnlyList<BattleMotionCadenceSample> trace, int fighter)
    {
        ProbeRequirement.Require(trace.Any(sample => sample.Blending), $"fighter={fighter} did not publish a blend");
        ProbeRequirement.Require(trace[1].Motion == 1, $"fighter={fighter} did not interrupt into idle motion");
        ProbeRequirement.Require(trace[^1].Motion == 1, $"fighter={fighter} did not return to idle after finish");
    }
}
