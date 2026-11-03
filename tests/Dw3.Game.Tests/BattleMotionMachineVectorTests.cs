using System.Text.Json.Nodes;
using Xunit;

namespace Dw3.Game.Tests;

/// <summary>
/// Differential regression suite for BattleMotionMachine. Replays every fabricated scenario of
/// Vectors/battle-motion-machine.json (see docs/specs/battle-motion-machine.md) and checks,
/// after the constructor and every operation, the outcome code, the public state snapshot and
/// the keyframe list whenever it is recorded.
/// </summary>
public sealed class BattleMotionMachineVectorTests
{
    private static readonly string VectorsPath =
        Path.Combine(AppContext.BaseDirectory, "Vectors", "battle-motion-machine.json");

    private static JsonArray LoadCases()
    {
        Assert.True(File.Exists(VectorsPath), $"Vectors file not found: {VectorsPath}");
        return JsonNode.Parse(File.ReadAllText(VectorsPath))!["cases"]!.AsArray();
    }

    [Fact]
    public void VectorCount_AtLeast300_AndCoversEveryOutcome()
    {
        var cases = LoadCases();
        Assert.True(cases.Count >= 300, $"Expected at least 300 vectors, got {cases.Count}");
        var codes = new HashSet<string>();
        foreach (var c in cases)
        {
            foreach (var o in c!["outcomes"]!.AsArray())
            {
                codes.Add(o!.GetValue<string>());
            }
        }

        foreach (var expected in new[]
                 {
                     "ok", "V:motion", "V:framesElapsed", "V:idleMotion", "E:InvalidOperationException",
                     "E:ArgumentOutOfRangeException", "E:*",
                 })
        {
            Assert.Contains(expected, codes);
        }
    }

    [Fact]
    public void AllVectorsMatchCurrentImplementation()
    {
        var errors = new List<string>();
        foreach (var c in LoadCases())
        {
            var id = c!["id"]!.GetValue<int>();
            try
            {
                Verify(c, id, errors);
            }
            catch (Exception ex)
            {
                errors.Add($"[{id}] replay threw {ex.GetType().Name}: {ex.Message}");
            }
        }

        if (errors.Count > 0)
        {
            Assert.Fail($"{errors.Count} mismatch(es):\n" + string.Join("\n", errors.Take(50)));
        }
    }

    private static void Verify(JsonNode c, int id, List<string> errors)
    {
        var motions = BattleMotionMachineVectorHarness.Motions(c["motions"]);
        var ops = c["ops"]!.AsArray().Select(o => o!.GetValue<string>()).ToList();
        var r = BattleMotionMachineVectorHarness.Run(motions, c["idle"]!.GetValue<int>(), ops);

        var outcomes = c["outcomes"]!.AsArray().Select(o => o!.GetValue<string>()).ToList();
        if (!outcomes.SequenceEqual(r.Outcomes))
        {
            errors.Add($"[{id}] outcomes: got [{string.Join(",", r.Outcomes)}] expected [{string.Join(",", outcomes)}]");
            return;
        }

        var snapshots = c["snapshots"]!.AsArray();
        var keyframes = c["keyframes"]!.AsArray();
        for (var i = 0; i < snapshots.Count; i++)
        {
            var label = i == 0 ? "ctor" : ops[i - 1];
            Compare(id, $"snapshot {i} after '{label}'", snapshots[i], r.Snapshots[i], errors);
            Compare(id, $"keyframes {i} after '{label}'", keyframes[i], r.Keyframes[i], errors);
        }
    }

    private static void Compare(int id, string what, JsonNode? expected, int[]? actual, List<string> errors)
    {
        var e = expected is null ? null : BattleMotionMachineVectorHarness.Ints(expected);
        var same = e is null ? actual is null : actual is not null && e.SequenceEqual(actual);
        if (!same)
        {
            errors.Add($"[{id}] {what}: got [{(actual is null ? "null" : string.Join(",", actual))}] expected [{(e is null ? "null" : string.Join(",", e))}]");
        }
    }
}
