using Dw3Production.Diagnostics.Field;
using Dw3Production.Presentation;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Checks the last boss's special entrance effects on the sampled production ticks.</summary>
internal static class BattleSpecialEntranceChecks
{
    /// <summary>Entrance frame of the first full-white unit, where the source swaps the model and applies extras.</summary>
    private const int SwapFrame = BattleEntranceSequence.LeadFrames + 65;

    /// <summary>Requires fighter 0x1D2's entrance to show stage 0x16 from the first full-white unit on, and the
    /// opening stage before it.</summary>
    /// <param name="samples">Played ticks of the entrance call.</param>
    /// <param name="openingStage">Stage the battle opened on.</param>
    public static void RequireStageChange(IReadOnlyList<BattleEntranceTickSample> samples, int openingStage)
    {
        ProbeRequirement.Require(openingStage != BattleEntranceExtras.EntranceStageId, $"entrance-1d2: the battle already opened on stage {openingStage}");
        foreach (var sample in samples.Where(sample => sample.Phase == nameof(BattleEntranceCallPhase.Sequence)))
        {
            int expected = sample.Frame >= SwapFrame ? BattleEntranceExtras.EntranceStageId : openingStage;
            ProbeRequirement.Require(sample.Stage == expected, $"entrance-1d2 frame={sample.Frame}: stage={sample.Stage} expected={expected}");
        }
    }

    /// <summary>Requires fighter 0x1D3's entrance to fade the last boss music out before full white and to start the
    /// second last boss music under it.</summary>
    /// <param name="samples">Played ticks of the entrance call.</param>
    public static void RequireMusicChange(IReadOnlyList<BattleEntranceTickSample> samples)
    {
        string first = $"{BattleEntranceExtras.LastBossMusic:X8}";
        string second = $"{BattleEntranceExtras.LastBossSecondMusic:X8}";
        var sequence = samples.Where(sample => sample.Phase == nameof(BattleEntranceCallPhase.Sequence)).ToList();
        ProbeRequirement.Require(sequence.Count > 0 && sequence[0].Music.StartsWith(first, StringComparison.Ordinal), $"entrance-1d3: opening music={sequence.FirstOrDefault()?.Music}");
        var beforeSwap = sequence.Where(sample => sample.Frame < SwapFrame).ToList();
        ProbeRequirement.Require(beforeSwap.Any(sample => sample.Music.StartsWith("00000000", StringComparison.Ordinal) || sample.Music.EndsWith(":0", StringComparison.Ordinal)), "entrance-1d3: the last boss music was not faded out before full white");
        ProbeRequirement.Require(sequence.Where(sample => sample.Frame >= SwapFrame).All(sample => sample.Music == $"{second}:1"), "entrance-1d3: the second last boss music did not play from full white");
    }
}
