using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Dw3Production.Conversion;
using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests;

/// <summary>
/// Generates tests/Dw3.Game.Tests/Vectors/climb-controller.json when the
/// environment variable GENERATE_VECTORS=1 is set.  Run via:
///   GENERATE_VECTORS=1 dotnet test --filter "GenerateClimbControllerVectors"
///
/// All inputs are fabricated (invented); no game values are embedded.
/// Outputs are captured from the current ClimbController and ClimbAnimation.Sample
/// implementations with a fixed seed and systematic boundary coverage.
/// </summary>
public sealed class ClimbControllerVectorGenerator
{
    // ------------------------------------------------------------------ generator
    [Fact]
    public void GenerateClimbControllerVectors()
    {
        if (Environment.GetEnvironmentVariable("GENERATE_VECTORS") != "1")
        {
            return;
        }

        var cases = new JsonArray();
        int id = 1;

        // ============================================================
        // Section A: ClimbAnimation.Sample cases
        // ============================================================

        // Standard animation data (inline per case for self-containment).
        // We test all 7 animation sets with 21 tick values each = 147 cases.
        var stdSets = new (int Set, bool Loops, ClimbRecord[] Records)[]
        {
            (0x1A, true,  [new ClimbRecord(0, 6, -21, -32), new ClimbRecord(1, 6, -21, -32)]),
            (0x1B, true,  [new ClimbRecord(1, 6, -21, -32)]),
            (0x1C, false, [new ClimbRecord(0, 4, -17, -29), new ClimbRecord(1, 4, -17, -29)]),
            (0x1D, false, [new ClimbRecord(0, 4, -21, -32)]),
            (0x1E, false, [new ClimbRecord(0, 4, -25, -49)]),
            (0x1F, false, [new ClimbRecord(0, 4, -20, -32)]),
            (0x20, true,  [new ClimbRecord(0, 6, -21, -32)]),
        };

        int[] sampleTicks =
        [
            0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 20, 50, 100, 999
        ];

        foreach (var (set, loops, records) in stdSets)
        {
            foreach (var ticks in sampleTicks)
            {
                cases.Add(AnimSampleCase(id++, set, ticks, loops, records));
            }
        }

        // ============================================================
        // Section B: ClimbSequence cases — full traversals
        // ============================================================
        int[] heights = [1, 2, 4, 8, 16, 32, 64, 128];
        int[] dirs = [0, 3, 5, 6];

        // Full ClimbUp (hold Up until done): 8 × 4 = 32 cases.
        // Max ticks = height + 20 (GetOnWall=8, ClimbOff=4, buffer=8).
        foreach (var h in heights)
        {
            foreach (var d in dirs)
            {
                int maxT = h + 20;
                cases.Add(ClimbSequenceCase(
                    id++,
                    "Up",
                    d,
                    100f,
                    100f,
                    h,
                    RunUntilDone("Up", maxT)));
            }
        }

        // Full ClimbDown (hold Down until done): 8 × 4 = 32 cases.
        // Max ticks = height + 20 (GetOverEdge=4, ClimbOff=4, buffer=12).
        foreach (var h in heights)
        {
            foreach (var d in dirs)
            {
                int maxT = h + 20;
                cases.Add(ClimbSequenceCase(
                    id++,
                    "Down",
                    d,
                    100f,
                    100f,
                    h,
                    RunUntilDone("Down", maxT)));
            }
        }

        // ============================================================
        // Section C: ClimbSequence cases — partial traversals
        // ============================================================

        // Partial 5-tick all-Up (still in GetOnWall for most heights): 8 × 4 = 32 cases.
        foreach (var h in heights)
        {
            foreach (var d in dirs)
            {
                cases.Add(ClimbSequenceCase(
                    id++,
                    "Up",
                    d,
                    50f,
                    80f,
                    h,
                    Repeat("Up", 5)));
            }
        }

        // Partial 15-tick all-Up (exits GetOnWall, begins climbing): 8 × 4 = 32 cases.
        foreach (var h in heights)
        {
            foreach (var d in dirs)
            {
                cases.Add(ClimbSequenceCase(
                    id++,
                    "Up",
                    d,
                    200f,
                    300f,
                    h,
                    Repeat("Up", 15)));
            }
        }

        // Partial 5-tick all-Down (still in GetOverEdge for all heights): 8 × 4 = 32 cases.
        foreach (var h in heights)
        {
            foreach (var d in dirs)
            {
                cases.Add(ClimbSequenceCase(
                    id++,
                    "Down",
                    d,
                    150f,
                    200f,
                    h,
                    Repeat("Down", 5)));
            }
        }

        // ============================================================
        // Section D: ClimbSequence cases — hold still and mixed inputs
        // ============================================================

        // Hold still (None) for 20 ticks after StartClimbUp: 4 heights × 4 dirs = 16 cases.
        foreach (var h in new[] { 4, 16, 32, 64 })
        {
            foreach (var d in dirs)
            {
                cases.Add(ClimbSequenceCase(
                    id++,
                    "Up",
                    d,
                    0f,
                    0f,
                    h,
                    Repeat("None", 20)));
            }
        }

        // Mixed Up/Down/None: 8 ticks up, 4 none, 4 down: 4 heights × 2 dirs = 8 cases.
        var mixedInputs = new string[16];
        for (int i = 0; i < 8; i++)
        {
            mixedInputs[i] = "Up";
        }

        for (int i = 8; i < 12; i++)
        {
            mixedInputs[i] = "None";
        }

        for (int i = 12; i < 16; i++)
        {
            mixedInputs[i] = "Down";
        }

        foreach (var h in new[] { 8, 32, 64, 128 })
        {
            foreach (var d in new[] { 0, 3 })
            {
                cases.Add(ClimbSequenceCase(id++, "Up", d, 100f, 100f, h, mixedInputs));
            }
        }

        // StartClimbDown then hold Up for 20 ticks: 4 heights × 2 dirs = 8 cases.
        foreach (var h in new[] { 8, 16, 32, 64 })
        {
            foreach (var d in new[] { 3, 6 })
            {
                cases.Add(ClimbSequenceCase(
                    id++,
                    "Down",
                    d,
                    100f,
                    100f,
                    h,
                    Repeat("Up", 20)));
            }
        }

        // ============================================================
        // Pad to reach 300 if needed (extra AnimSample with additional tick values).
        // ============================================================
        int[] extraTicks = [17, 18, 19, 21, 22, 23, 24, 25, 30, 40, 60, 75, 200, 500];
        int padIdx = 0;
        while (cases.Count < 300)
        {
            var (set, loops, records) = stdSets[padIdx % stdSets.Length];
            int t = extraTicks[padIdx % extraTicks.Length];
            cases.Add(AnimSampleCase(id++, set, t, loops, records));
            padIdx++;
        }

        // ============================================================
        // Write output file.
        // ============================================================
        var root = new JsonObject
        {
            ["metadata"] = new JsonObject
            {
                ["seed"] = 42,
                ["count"] = cases.Count,
                ["note"] = "Fabricated differential vectors for ClimbController and " +
                            "ClimbAnimation.Sample. All inputs are invented; no game values " +
                            "are embedded. Generated from the current implementation.",
                ["standardDataNote"] = "ClimbSequence cases use a fixed animation data set: " +
                    "GetOnWall(28)=8ticks, GetOverEdge(30)=4ticks, ClimbOffTop(29)=4ticks, " +
                    "ClimbOffBottom(31)=4ticks. Looping sets: ClimbUp(26)=12ticks-cycle, " +
                    "ClimbDown(27)=6ticks-cycle, Climb(32)=6ticks-cycle.",
            },
            ["cases"] = cases,
        };

        var outPath = Path.Combine(
            Path.GetDirectoryName(typeof(ClimbControllerVectorGenerator).Assembly.Location)!,
            "Vectors",
            "climb-controller.json");
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        File.WriteAllText(outPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

        Assert.True(cases.Count >= 300, $"Generated {cases.Count} vectors, need at least 300.");
    }

    // ------------------------------------------------------------------ standard ClimbData

    /// <summary>
    /// Standard fabricated animation data used for all ClimbSequence test cases.
    /// Set ids match the controller's animation set constants.
    /// Durations are chosen to exercise the IsAnimDone transitions with known tick counts:
    ///   GetOnWall (28) = 8 ticks total, GetOverEdge (30) = 4 ticks,
    ///   ClimbOffTop (29) = 4 ticks, ClimbOffBottom (31) = 4 ticks.
    /// Looping sets (Climb=32, ClimbUp=26, ClimbDown=27) cycle forever.
    /// </summary>
    private static ClimbData StandardData()
    {
        var sequences = new Dictionary<int, ClimbSequence>
        {
            [0x1A] = new(0x1A, true, [new ClimbRecord(0, 6, -21, -32), new ClimbRecord(1, 6, -21, -32)]),
            [0x1B] = new(0x1B, true, [new ClimbRecord(1, 6, -21, -32)]),
            [0x1C] = new(0x1C, false, [new ClimbRecord(0, 4, -17, -29), new ClimbRecord(1, 4, -17, -29)]),
            [0x1D] = new(0x1D, false, [new ClimbRecord(0, 4, -21, -32)]),
            [0x1E] = new(0x1E, false, [new ClimbRecord(0, 4, -25, -49)]),
            [0x1F] = new(0x1F, false, [new ClimbRecord(0, 4, -20, -32)]),
            [0x20] = new(0x20, true, [new ClimbRecord(0, 6, -21, -32)]),
        };
        return new ClimbData(sequences);
    }

    // ------------------------------------------------------------------ helpers
    private static InputState MakeInput(string label)
    {
        var inp = new InputState();
        switch (label)
        {
            case "Up": inp.SetDirections(left: false, right: false, up: true, down: false); break;
            case "Down": inp.SetDirections(left: false, right: false, up: false, down: true); break;
            case "None": inp.SetDirections(left: false, right: false, up: false, down: false); break;
            default: throw new ArgumentException($"Unknown input label: {label}");
        }

        return inp;
    }

    private static JsonArray ToInputArray(IEnumerable<string> inputs)
    {
        var arr = new JsonArray();
        foreach (var s in inputs)
        {
            arr.Add(s);
        }

        return arr;
    }

    // ------------------------------------------------------------------ AnimSample case builder
    private static JsonObject AnimSampleCase(
        int id,
        int set,
        int ticks,
        bool loops,
        IReadOnlyList<ClimbRecord> records)
    {
        // Build inline ClimbData for this case only.
        var seqDict = new Dictionary<int, ClimbSequence>
        {
            [set] = new ClimbSequence(set, loops, records),
        };
        var data = new ClimbData(seqDict);
        var result = ClimbAnimation.Sample(data, set, ticks);

        var recordsJson = new JsonArray();
        foreach (var r in records)
        {
            recordsJson.Add(new JsonObject
            {
                ["frame"] = r.Frame,
                ["duration"] = r.Duration,
                ["offsetX"] = r.OffsetX,
                ["offsetY"] = r.OffsetY,
            });
        }

        return new JsonObject
        {
            ["id"] = id,
            ["fn"] = "AnimSample",
            ["set"] = set,
            ["loops"] = loops,
            ["records"] = recordsJson,
            ["ticks"] = ticks,
            ["expFrame"] = result.Frame,
            ["expOffsetX"] = result.OffsetX,
            ["expOffsetY"] = result.OffsetY,
        };
    }

    // ------------------------------------------------------------------ ClimbSequence case builder
    private static JsonObject ClimbSequenceCase(
        int id,
        string startMethod,
        int dir,
        float x,
        float y,
        int height,
        string[] inputs)
    {
        var state = new GameState { Position = new Vector2(x, y) };
        var ctrl = new ClimbController();
        ctrl.SetData(StandardData());

        if (startMethod == "Up")
        {
            ctrl.StartClimbUp(state, dir, (int)x, (int)y, height);
        }
        else
        {
            ctrl.StartClimbDown(state, dir, (int)x, (int)y, height);
        }

        foreach (var label in inputs)
        {
            if (!state.ClimbActive)
            {
                break;  // stop when the climb has ended
            }

            ctrl.Tick(state, MakeInput(label));
        }

        return new JsonObject
        {
            ["id"] = id,
            ["fn"] = "ClimbSequence",
            ["startMethod"] = startMethod,
            ["dir"] = dir,
            ["x"] = x,
            ["y"] = y,
            ["height"] = height,
            ["inputs"] = ToInputArray(inputs),
            ["expClimbActive"] = state.ClimbActive,
            ["expClimbHeight"] = state.ClimbHeight,
            ["expClimbSide"] = state.ClimbSide,
            ["expAnimSet"] = state.ClimbAnimationSet,
            ["expAnimTicks"] = state.ClimbAnimationTicks,
            ["expAnimDirection"] = state.AnimationDirection,
            ["expPosX"] = state.Position.X,
            ["expPosY"] = state.Position.Y,
            ["expFacing"] = state.Facing,
            ["expCameraSmoothing"] = state.CameraSmoothingTicks,
            ["expOffer"] = state.Offer,
            ["expStateAnimSet"] = state.AnimationSet,
            ["expStateAnimTicks"] = state.AnimationTicks,
        };
    }

    // Convenience: run exactly tickCount ticks all with the same input label.
    private static string[] Repeat(string label, int count)
    {
        var arr = new string[count];
        for (int i = 0; i < count; i++)
        {
            arr[i] = label;
        }

        return arr;
    }

    // Run ticks until ClimbActive becomes false OR maxTicks is reached.
    private static string[] RunUntilDone(string label, int maxTicks)
        => Repeat(label, maxTicks); // The controller stops writing when inactive; the test just runs all ticks.
}
