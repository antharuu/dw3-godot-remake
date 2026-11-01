using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace Dw3.Game.Tests;

/// <summary>
/// Generates tests/Dw3.Game.Tests/Vectors/battle-motion-machine.json when GENERATE_VECTORS=1:
///   GENERATE_VECTORS=1 GENERATE_VECTORS_OUT=$PWD/tests/Dw3.Game.Tests/Vectors/battle-motion-machine.json \
///     dotnet test tests/Dw3.Game.Tests --filter "FullyQualifiedName~GenerateBattleMotionMachineVectors"
///
/// Every input (motion tables, constructor arguments, operation lists) is fabricated from a
/// fixed seed; the expected outputs are produced by the current BattleMotionMachine.
/// </summary>
public sealed class BattleMotionMachineVectorGenerator
{
    private const int Seed = 20261101;
    private const int End = 0x7FFF;

    private sealed class Scenario(string family, int[][][] motions, int idle, List<string> ops)
    {
        public string Family { get; } = family;
        public int[][][] Motions { get; } = motions;
        public int Idle { get; } = idle;
        public List<string> Ops { get; } = ops;
    }

    /// <summary>Builds one well-formed motion: explicit and blended runs, then a terminator.</summary>
    private static int[][] RandomMotion(Random r, int? forcedTerminator = null, bool allowIdleTarget = true)
    {
        var steps = new List<int[]>();
        var keyframes = 0; // keyframes produced so far
        var segments = r.Next(1, 6);
        var lastWasBlend = false;
        for (var s = 0; s < segments; s++)
        {
            var count = r.Next(1, 11);
            var blend = steps.Count > 0 && r.Next(10) < 4;
            if (s == 0 && r.Next(6) == 0) blend = true; // a leading blend must use source "current pose"
            if (blend)
            {
                var index = steps.Count == 0 || r.Next(4) == 0 ? 0 : steps.Count;
                steps.Add([index, Math.Min(count, 8), r.Next(0, 300), 0]);
            }
            else
            {
                var frame = r.Next(0, 300);
                var endFrame = r.Next(3) == 0 ? r.Next(1, 300) : Math.Max(1, frame + count - 1);
                steps.Add([steps.Count, count, frame, endFrame]);
            }
            keyframes += steps[^1][1];
            lastWasBlend = blend;
        }

        var kind = forcedTerminator ?? r.Next(7);
        if (kind == 2 && !allowIdleTarget) kind = 3;
        var pos = steps.Count;
        int[] terminator = kind switch
        {
            0 => [End, r.Next(0, 5), r.Next(0, 300), r.Next(0, 300)],
            1 => [pos, 0, 0x8000, r.Next(0, keyframes + 1)],                    // loop
            2 => [pos, 0, -1, 0],                                               // finish (and idle target)
            3 => [pos, 0, 0xFFFF, 0],                                           // finish, literal
            4 => [pos, 0, r.Next(0, 300), r.Next(0, 300)],                      // plain held frame
            5 => [pos, 0, 0x8000 | r.Next(1, 0x8000), r.Next(0, 300)],          // blended hold
            _ => [pos, 0, 0x8000, 0],                                           // loop to entry 0
        };
        // A blend run right before a 0x7FFF end marker needs a successor; give it a real one.
        if (kind == 0 && lastWasBlend) steps.Add([pos, r.Next(1, 4), r.Next(0, 300), 7]);
        steps.Add(terminator);
        if (r.Next(5) == 0) steps.Add([r.Next(0, 9), r.Next(0, 9), r.Next(0, 300), r.Next(0, 300)]); // ignored tail
        return [.. steps];
    }

    private static int[][][] RandomTable(Random r, int min = 1, int max = 6)
    {
        var n = r.Next(min, max);
        var table = new int[n][][];
        for (var i = 0; i < n; i++)
            table[i] = RandomMotion(r, i < 2 && r.Next(3) > 0 ? 1 : null);
        return table;
    }

    private static string RandomOp(Random r, int motionCount)
    {
        var roll = r.Next(100);
        return roll switch
        {
            < 45 => "s:1",
            < 50 => "s:0",
            < 64 => $"s:{r.Next(2, 7)}",
            < 67 => $"s:{r.Next(20, 500)}",
            < 82 => $"m:{r.Next(1, motionCount + 1)}:{r.Next(2)}",
            < 87 => $"m:{r.Next(1, Math.Min(2, motionCount) + 1)}:0",
            < 90 => $"m:{(r.Next(2) == 0 ? r.Next(-3, 1) : motionCount + r.Next(1, 4))}:{r.Next(2)}",
            < 96 => $"i:{r.Next(2)}",
            < 98 => $"i:{(r.Next(2) == 0 ? -1 : r.Next(2, 5))}",
            _ => $"s:{r.Next(-4, 0)}",
        };
    }

    private static List<string> RandomOps(Random r, int motionCount, int length)
    {
        var ops = new List<string>();
        for (var i = 0; i < length; i++) ops.Add(RandomOp(r, motionCount));
        return ops;
    }

    private static List<string> Repeat(string op, int n) => [.. Enumerable.Repeat(op, n)];

    private static IEnumerable<Scenario> Edges()
    {
        int[][] Loop(params int[][] runs) => [.. runs, [runs.Length, 0, 0x8000, 0]];
        var idle = Loop([0, 4, 10, 13]);
        var idle2 = Loop([0, 3, 50, 52]);
        List<string> Ops(params string[] o) => [.. o];

        // Interpolation boundaries: run lengths whose 4096-scaled ratios reach 0.
        foreach (var count in new[] { 1, 2, 3, 7, 4095, 4096, 4097, 5000 })
            yield return new("edge-interp", [idle, [[0, 2, 5, 6], [1, count, 0, 0], [2, 2, 80, 81], [3, 0, 0xFFFF, 0]]], 0,
                [.. Ops("m:2:1"), .. Repeat("s:1", 8), "s:3000", "s:1", "s:1500", "s:1", "s:1", "s:1", "s:1"]);

        // Frames carrying bit 15 (negative or >= 0x8000) are read as blends; reaching 0xFFFF finishes.
        yield return new("edge-bit15", [[[0, 6, -3, 2], [1, 0, 0x8000, 1]]], 0, Repeat("s:1", 12));
        yield return new("edge-bit15", [[[0, 5, 0x7FFE, 1], [1, 0, 7, 0]]], 0, Repeat("s:1", 8));
        yield return new("edge-bit15", [[[0, 4, 0xFFFD, 1], [1, 0, 3, 0]]], 0, Repeat("s:1", 8));
        yield return new("edge-bit15", [[[0, 3, 0x18000, 1], [1, 0, 0x8000, 0]]], 0, Repeat("s:1", 8));
        yield return new("edge-bit15", [[[0, 3, -0x10000 + 0x7FFF, 1], [1, 0, 0x8000, 0]]], 0, Repeat("s:1", 8));

        // Terminator frame masking: only the low 16 bits are kept.
        yield return new("edge-mask", [[[0, 3, 1, 3], [1, 0, 0x18000, 2]]], 0, Repeat("s:1", 8));
        yield return new("edge-mask", [[[0, 3, 1, 3], [1, 0, 0x2FFFF, 0]]], 0, Repeat("s:1", 6));
        yield return new("edge-mask", [[[0, 3, 1, 3], [1, 0, -0x8000, 1]]], 0, Repeat("s:1", 8));

        // Loop jumps: to entry 0, to itself, to the last entry, chained markers, beyond the list.
        yield return new("edge-loop", [[[0, 4, 20, 23], [1, 0, 0x8000, 4]]], 0, Repeat("s:1", 10));
        yield return new("edge-loop", [[[0, 4, 20, 23], [1, 0, 0x8000, 0]]], 0, [.. Repeat("s:1", 10), "s:0", "s:7", "s:2"]);
        yield return new("edge-loop", [[[0, 4, 20, 23], [1, 0, 0x8000, 3]]], 0, [.. Repeat("s:1", 10), "s:0"]);
        yield return new("edge-loop", [[[0, 4, 20, 23], [1, 0, 0x8000, 9]]], 0, Repeat("s:1", 8));
        yield return new("edge-loop", [[[0, 4, 20, 23], [1, 0, 0x8000, -1]]], 0, Repeat("s:1", 8));
        yield return new("edge-loop", [[[0, 1, 0x8000, 1], [1, 3, 9, 11], [2, 0, 0x8000, 0]]], 0, Repeat("s:1", 10));

        // A plain held terminator and a 0x7FFF end marker keep clamping on the last entry.
        yield return new("edge-hold", [[[0, 3, 40, 42], [1, 0, 99, 0]]], 0, [.. Repeat("s:1", 6), "s:100", "s:0"]);
        yield return new("edge-hold", [[[0, 3, 40, 42], [End, 9, 9, 9], [0, 0, 0xFFFF, 0]]], 0, [.. Repeat("s:1", 6), "s:50"]);

        // Blend sources: current pose (index 0), previous run end frame, unchanged target keeps source.
        yield return new("edge-source", [idle, [[0, 2, 0, 0], [0, 3, 30, 32], [2, 2, 0, 0], [3, 0, 30, 0], [4, 0, 0xFFFF, 0]]], 0,
            [.. Ops("m:2:1"), .. Repeat("s:1", 12)]);
        yield return new("edge-source", [[[0, 3, 0, 0], [1, 0, 0, 0]]], 0, Repeat("s:1", 6));
        yield return new("edge-source", [[[0, 3, 5, 7], [1, 3, 9, 0], [2, 0, 0, 0]]], 0, Repeat("s:1", 8));

        // Idle return: the motion's -1 target is the first frame of motion 1 or 2 per idle selection.
        yield return new("edge-idle", [idle, idle2, [[0, 3, 100, 102], [1, 3, 0, 0], [2, 0, -1, 0]]], 0,
            [.. Ops("m:3:1"), .. Repeat("s:1", 10), "i:1", "s:1", "s:1"]);
        yield return new("edge-idle", [idle, idle2, [[0, 3, 100, 102], [1, 3, 0, 0], [2, 0, -1, 0]]], 1,
            [.. Ops("m:3:1"), .. Repeat("s:1", 10)]);
        yield return new("edge-idle", [idle, idle2, [[0, 3, 100, 102], [1, 3, 0, 0], [2, 0, -1, 0]]], 0,
            [.. Ops("i:1", "m:3:1", "i:0"), .. Repeat("s:1", 10)]);
        yield return new("edge-idle", [idle, idle2, [[0, 3, 100, 102], [1, 3, 0, 0], [2, 0, -1, 0]]], 0,
            [.. Ops("m:2:1", "m:3:1"), .. Repeat("s:1", 10)]);
        // Only one motion: the second idle frame defaults to 0.
        yield return new("edge-idle", [[[0, 2, 7, 8], [1, 2, 0, 0], [2, 0, -1, 0]]], 0, [.. Ops("i:1", "m:1:1"), .. Repeat("s:1", 8)]);
        // Motion 1 finishing toward idle returns to itself only through the no-restart rule: it stays done.
        yield return new("edge-idle", [[[0, 2, 7, 8], [1, 2, 0, 0], [2, 0, -1, 0]], idle2], 0, Repeat("s:1", 9));
        yield return new("edge-idle", [idle, [[0, 2, 7, 8], [1, 2, 0, 0], [2, 0, -1, 0]]], 1, Repeat("s:1", 9));
        // A finish without idle target stays finished forever.
        yield return new("edge-idle", [idle, [[0, 3, 7, 9], [1, 0, 0xFFFF, 0]]], 0, [.. Ops("m:2:1"), .. Repeat("s:1", 6), "s:9", "m:2:0", "m:2:1", "s:1"]);
        // Idle frame of an empty first step list is 0.
        yield return new("edge-idle", [idle, [], [[0, 2, 9, 10], [1, 2, 0, 0], [2, 0, -1, 0]]], 1, Repeat("s:1", 1));
        yield return new("edge-idle", [idle, [[End, 0, 0, 0]], [[0, 2, 9, 10], [1, 2, 0, 0], [2, 0, -1, 0]]], 0,
            [.. Ops("i:1", "m:3:1"), .. Repeat("s:1", 7)]);

        // SetMotion rules.
        yield return new("edge-setmotion", [idle, idle2, [[0, 5, 60, 64], [1, 0, 0xFFFF, 0]]], 0,
            Ops("s:1", "s:1", "m:1:0", "m:1:1", "s:2", "m:3:0", "s:1", "m:3:0", "s:1", "m:3:1", "m:2:0", "m:1:0", "m:0:0", "m:4:1", "m:-5:1", "m:2:0"));
        yield return new("edge-setmotion", [idle, idle2], 1, Ops("s:1", "m:2:0", "m:1:0", "i:1", "m:2:1", "i:0", "m:1:1"));

        // Constructor arguments.
        yield return new("edge-ctor", [idle, idle2, [[0, 2, 1, 2], [1, 0, 0x8000, 0]]], 2, Ops("s:1", "s:1", "m:3:1", "i:1", "m:3:1"));
        yield return new("edge-ctor", [idle, idle2], 2, Ops("s:1"));
        yield return new("edge-ctor", [idle, idle2], -1, Ops("s:1"));
        yield return new("edge-ctor", [idle], 1, Ops("s:1"));
        yield return new("edge-ctor", [[[End, 0, 0, 0]]], 0, Ops("s:1"));
        yield return new("edge-ctor", [[]], 0, Ops("s:1"));
        yield return new("edge-ctor", [[[0, -2, 4, 5], [1, 0, 6, 0]]], 0, Repeat("s:1", 3));
        yield return new("edge-ctor", [[[0, 0, 6, 0]]], 0, Ops("s:0"));
        yield return new("edge-ctor", [[[0, 0, 0x8000, 0]]], 0, Ops("s:1"));

        // A motion without keyframes on SetMotion.
        yield return new("edge-empty", [idle, [[End, 1, 2, 3]]], 0, Ops("s:1", "m:2:1", "s:1"));
        yield return new("edge-empty", [idle, [[0, -1, 0, 9], [End, 0, 0, 0]]], 0, Ops("m:2:1"));

        // Malformed data: missing successor, leading blend with a non-zero index, short step, idle index.
        yield return new("edge-malformed", [idle, [[0, 2, 5, 6], [1, 3, 0, 0]]], 0, Ops("s:1", "m:2:1"));
        yield return new("edge-malformed", [idle, [[3, 2, 5, 0], [1, 0, 0xFFFF, 0]]], 0, Ops("m:2:1"));
        yield return new("edge-malformed", [idle, [[0, 2, 5]]], 0, Ops("m:2:1"));
        yield return new("edge-malformed", [[[0, 2]]], 0, Ops("s:1"));
        yield return new("edge-malformed", [idle, idle2, [[0, 2, 5, 6], [1, 2, 0, 0], [2, 0, -1, 0]]], 2, Ops("m:3:1"));

        // Step arguments: zero, huge elapsed counts, 32-bit wraparound, negative.
        yield return new("edge-elapsed", [idle], 0, Ops("s:0", "s:0", "s:2147483647", "s:1", "s:0"));
        yield return new("edge-elapsed", [[[0, 4, 1, 4], [1, 0, 0xFFFF, 0]]], 0, Ops("s:2147483646", "s:1"));
        yield return new("edge-elapsed", [[[0, 4, 1, 4], [1, 0, 0xFFFF, 0]]], 0, Ops("s:2147483647"));
        yield return new("edge-elapsed", [[[0, 4, 1, 4], [1, 0, 0xFFFF, 0]]], 0, Ops("s:-1", "s:-2147483648", "s:1"));
        yield return new("edge-elapsed", [[[0, 3, 1, 4], [1, 0, 0x8000, 0]]], 0, Ops("s:1", "s:1", "s:1", "s:2147483647", "s:1"));

        // SetIdleMotion validation.
        yield return new("edge-idlearg", [idle, idle2], 0, Ops("i:2", "i:-1", "i:1", "i:0", "i:2147483647", "i:-2147483648"));
        // Single-entry keyframe list: the first Step reads past the end.
        yield return new("edge-single", [[[0, 1, 5, 5], [End, 0, 0, 0]]], 0, Ops("s:0"));
    }

    [Fact]
    public void GenerateBattleMotionMachineVectors()
    {
        if (Environment.GetEnvironmentVariable("GENERATE_VECTORS") != "1") return;

        var random = new Random(Seed);
        var scenarios = new List<Scenario>();

        // 1. Random well-formed tables, random interleavings of every operation.
        for (var n = 0; n < 160; n++)
        {
            var table = RandomTable(random);
            var idle = table.Length >= 2 && random.Next(4) == 0 ? 1 : 0;
            scenarios.Add(new("random-play", table, idle, RandomOps(random, table.Length, random.Next(15, 41))));
        }

        // 2. Idle return: an action ending on a -1 target, played to completion and beyond.
        for (var n = 0; n < 60; n++)
        {
            var table = new[] { RandomMotion(random, 1), RandomMotion(random, random.Next(2) == 0 ? 1 : 6), RandomMotion(random, 2) };
            var ops = new List<string>();
            if (random.Next(3) == 0) ops.Add($"i:{random.Next(2)}");
            if (random.Next(3) == 0) ops.Add($"m:{random.Next(1, 3)}:{random.Next(2)}");
            ops.Add("m:3:1");
            for (var i = 0; i < 70; i++)
            {
                var roll = random.Next(12);
                ops.Add(roll switch { 0 => $"i:{random.Next(2)}", 1 => "s:0", 2 => $"s:{random.Next(2, 5)}", _ => "s:1" });
            }
            scenarios.Add(new("idle-return", table, random.Next(2), ops));
        }

        // 3. Looping motions driven through many cycles with mixed elapsed counts.
        for (var n = 0; n < 50; n++)
        {
            var table = new[] { RandomMotion(random, random.Next(2) == 0 ? 1 : 6) };
            var ops = new List<string>();
            for (var i = 0; i < 60; i++)
                ops.Add(random.Next(5) == 0 ? $"s:{random.Next(0, 12)}" : "s:1");
            scenarios.Add(new("loop", table, 0, ops));
        }

        // 4. Hand-picked bounds and failure paths.
        scenarios.AddRange(Edges());

        var sb = new StringBuilder();
        sb.Append("{\n  \"metadata\": ");
        sb.Append(new JsonObject
        {
            ["seed"] = Seed,
            ["count"] = scenarios.Count,
            ["note"] = "Fabricated vectors generated from the current BattleMotionMachine implementation. All inputs are invented; no game values are embedded.",
            ["snapshotLayout"] = "Motion, IdleMotion, Keyframe, Frame, Blend, Blending, MotionDone, ToIdle, BlendTarget, BlendSource, Looping",
            ["ops"] = "\"s:N\" = Step(N), \"m:M:R\" = SetMotion(M, restart: R == 1), \"i:K\" = SetIdleMotion(K)",
            ["outcomes"] = "index 0 = constructor; \"ok\"; \"V:param\" = ArgumentOutOfRangeException(param), state unchanged, continue; \"E:type\" = stop with that exception type; \"E:*\" = stop with any exception",
            ["keyframes"] = "full keyframe list after the constructor, then only when it changed (null otherwise)",
        }.ToJsonString());
        sb.Append(",\n  \"cases\": [\n");
        for (var i = 0; i < scenarios.Count; i++)
        {
            var s = scenarios[i];
            var r = BattleMotionMachineVectorHarness.Run(s.Motions, s.Idle, s.Ops);
            var executed = s.Ops.Take(r.Outcomes.Count - 1).ToList();
            var obj = new JsonObject
            {
                ["id"] = i + 1,
                ["family"] = s.Family,
                ["motions"] = new JsonArray(s.Motions.Select(m => (JsonNode)new JsonArray(m.Select(st => (JsonNode)Arr(st)).ToArray())).ToArray()),
                ["idle"] = s.Idle,
                ["ops"] = new JsonArray(executed.Select(o => (JsonNode)o!).ToArray()),
                ["outcomes"] = new JsonArray(r.Outcomes.Select(o => (JsonNode)o!).ToArray()),
                ["snapshots"] = new JsonArray([.. r.Snapshots.Select(v => v is null ? null : (JsonNode)Arr(v))]),
                ["keyframes"] = new JsonArray([.. r.Keyframes.Select(v => v is null ? null : (JsonNode)Arr(v))]),
            };
            sb.Append("    ").Append(obj.ToJsonString());
            sb.Append(i + 1 < scenarios.Count ? ",\n" : "\n");
        }
        sb.Append("  ]\n}\n");

        var outPath = Environment.GetEnvironmentVariable("GENERATE_VECTORS_OUT") is { Length: > 0 } p
            ? p
            : Path.Combine(AppContext.BaseDirectory, "Vectors", "battle-motion-machine.json");
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        File.WriteAllText(outPath, sb.ToString());

        Assert.True(scenarios.Count >= 300, $"Generated {scenarios.Count} vectors, need at least 300.");
    }

    private static JsonArray Arr(int[] a) => [.. a.Select(v => (JsonNode)v).ToArray()];
}
