using System.Text.Json.Nodes;
using Xunit;

namespace Dw3.Game.Tests;

/// <summary>
/// Differential regression suite for BattleShotCamera. Replays every fabricated scenario of
/// Vectors/battle-shot-camera.json (see docs/specs/battle-shot-camera.md) and checks, after
/// each operation, the public state snapshot, the provider calls made and any exception.
/// Recorded views are served back to the camera in issue order; asking for more views than
/// were recorded is itself a mismatch.
/// </summary>
public sealed class BattleShotCameraVectorTests
{
    private static readonly string VectorsPath =
        Path.Combine(AppContext.BaseDirectory, "Vectors", "battle-shot-camera.json");

    [Fact]
    public void VectorCount_AtLeast300()
    {
        var cases = LoadCases();
        Assert.True(cases.Count >= 300, $"Expected at least 300 vectors, got {cases.Count}");

        // Every reachable behaviour and both exception-free and failing paths are covered.
        var substates = new HashSet<int>();
        var failures = 0;
        foreach (var c in cases)
        {
            foreach (var s in c!["snapshots"]!.AsArray())
            {
                substates.Add(s![3]!.GetValue<int>());
            }

            if (c["exception"] is not null)
            {
                failures++;
            }
        }

        Assert.Equal([1, 3, 4, 5, 6, 7, 9, 10], [.. substates.OrderBy(v => v)]);
        Assert.True(failures > 0, "expected at least one failing-RNG scenario");
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

    private static JsonArray LoadCases()
    {
        Assert.True(File.Exists(VectorsPath), $"Vectors file not found: {VectorsPath}");
        return JsonNode.Parse(File.ReadAllText(VectorsPath))!["cases"]!.AsArray();
    }

    private static void Verify(JsonNode c, int id, List<string> errors)
    {
        var rng = BattleShotCameraVectorHarness.Ints(c["rng"]);
        var ops = c["ops"]!.AsArray().Select(o => o!.GetValue<string>()).ToList();
        Func<int[]>? enemy = null, fighter = null;
        if (c["ctor"]!.GetValue<string>() == "views")
        {
            enemy = Replay(c["enemyViews"]!.AsArray(), "enemy");
            fighter = Replay(c["fighterViews"]!.AsArray(), "fighter");
        }

        var r = BattleShotCameraVectorHarness.Run(rng, enemy, fighter, ops);

        var expectedThrowsAt = c["throwsAt"]!.GetValue<int>();
        var expectedException = c["exception"]?.GetValue<string>();
        if (r.ThrowsAt != expectedThrowsAt || r.Exception != expectedException)
        {
            errors.Add($"[{id}] exception: got {r.Exception}@{r.ThrowsAt} expected {expectedException}@{expectedThrowsAt}");
        }

        var snapshots = c["snapshots"]!.AsArray();
        if (snapshots.Count != r.Snapshots.Count)
        {
            errors.Add($"[{id}] snapshot count: got {r.Snapshots.Count} expected {snapshots.Count}");
            return;
        }

        for (var i = 0; i < snapshots.Count; i++)
        {
            var expected = BattleShotCameraVectorHarness.Ints(snapshots[i]);
            if (!expected.SequenceEqual(r.Snapshots[i]))
            {
                errors.Add($"[{id}] snapshot {i} after op '{(i == 0 ? "ctor" : ops[i - 1])}': got [{string.Join(",", r.Snapshots[i])}] expected [{string.Join(",", expected)}]");
            }
        }

        var calls = c["calls"]!.AsArray();
        for (var i = 0; i < calls.Count && i < r.Calls.Count; i++)
        {
            var expected = calls[i]!.AsArray().Select(x => x!.GetValue<string>()).ToArray();
            if (!expected.SequenceEqual(r.Calls[i]))
            {
                errors.Add($"[{id}] calls {i}: got [{string.Join(",", r.Calls[i])}] expected [{string.Join(",", expected)}]");
            }
        }

        if (calls.Count != r.Calls.Count)
        {
            errors.Add($"[{id}] call-group count: got {r.Calls.Count} expected {calls.Count}");
        }
    }

    private static Func<int[]> Replay(JsonArray views, string name)
    {
        var index = 0;
        return () =>
        {
            if (index >= views.Count)
            {
                throw new InvalidOperationException($"{name} provider asked for view #{index + 1}, only {views.Count} recorded");
            }

            return BattleShotCameraVectorHarness.Ints(views[index++]);
        };
    }
}
