using System.Numerics;
using System.Text.Json.Nodes;
using Dw3Production.Conversion;
using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests;

/// <summary>
/// Differential regression suite for ClimbController and ClimbAnimation.Sample.
/// Reads 300+ fabricated test vectors from Vectors/climb-controller.json,
/// replays each against the current implementation, and asserts the output
/// matches the recorded expected value.
///
/// Vectors were generated from the current implementation; no game values
/// are embedded.  All inputs are invented.
/// </summary>
public sealed class ClimbControllerVectorTests
{
    private static readonly string VectorsPath = Path.Combine(
        AppContext.BaseDirectory, "Vectors", "climb-controller.json");

    // ------------------------------------------------------------------ count check
    [Fact]
    public void VectorCount_AtLeast300()
    {
        Assert.True(
            File.Exists(VectorsPath),
            $"Vectors file not found: {VectorsPath}");

        var doc = JsonNode.Parse(File.ReadAllText(VectorsPath))!;
        int count = doc["cases"]!.AsArray().Count;
        Assert.True(
            count >= 300,
            $"Expected at least 300 vectors, got {count}");
    }

    // ------------------------------------------------------------------ main replay
    [Fact]
    public void AllVectorsMatchCurrentImplementation()
    {
        Assert.True(
            File.Exists(VectorsPath),
            $"Vectors file not found: {VectorsPath}");

        var doc = JsonNode.Parse(File.ReadAllText(VectorsPath))!;
        var cases = doc["cases"]!.AsArray();
        var errors = new List<string>();

        foreach (var c in cases)
        {
            int caseId = c!["id"]!.GetValue<int>();
            string fn = c["fn"]!.GetValue<string>();

            try
            {
                switch (fn)
                {
                    case "AnimSample":
                        VerifyAnimSample(c, caseId, errors);
                        break;

                    case "ClimbSequence":
                        VerifyClimbSequence(c, caseId, errors);
                        break;

                    default:
                        errors.Add($"[{caseId}] Unknown fn '{fn}'");
                        break;
                }
            }
            catch (Exception ex)
            {
                errors.Add($"[{caseId}] {fn} threw {ex.GetType().Name}: {ex.Message}");
            }
        }

        if (errors.Count > 0)
        {
            Assert.Fail($"{errors.Count} vector(s) failed:\n" + string.Join("\n", errors));
        }
    }

    // ------------------------------------------------------------------ standard ClimbData
    // Must match the data used in the generator exactly.
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

    private static ClimbData AnimSampleDataFromCase(JsonNode c)
    {
        int set = c["set"]!.GetValue<int>();
        bool loops = c["loops"]!.GetValue<bool>();
        var recs = new List<ClimbRecord>();
        foreach (var r in c["records"]!.AsArray())
        {
            recs.Add(new ClimbRecord(
                r!["frame"]!.GetValue<int>(),
                r["duration"]!.GetValue<int>(),
                r["offsetX"]!.GetValue<int>(),
                r["offsetY"]!.GetValue<int>()));
        }

        var seq = new ClimbSequence(set, loops, recs);
        var seqDict = new Dictionary<int, ClimbSequence> { [set] = seq };
        return new ClimbData(seqDict);
    }

    // ------------------------------------------------------------------ AnimSample verifier
    private static void VerifyAnimSample(JsonNode c, int id, List<string> errors)
    {
        var data = AnimSampleDataFromCase(c);
        int set = c["set"]!.GetValue<int>();
        int ticks = c["ticks"]!.GetValue<int>();

        var result = ClimbAnimation.Sample(data, set, ticks);

        int expFrame = c["expFrame"]!.GetValue<int>();
        int expOffsetX = c["expOffsetX"]!.GetValue<int>();
        int expOffsetY = c["expOffsetY"]!.GetValue<int>();

        if (result.Frame != expFrame)
        {
            errors.Add($"[{id}] AnimSample set={set} ticks={ticks}: Frame got {result.Frame} expected {expFrame}");
        }

        if (result.OffsetX != expOffsetX)
        {
            errors.Add($"[{id}] AnimSample set={set} ticks={ticks}: OffsetX got {result.OffsetX} expected {expOffsetX}");
        }

        if (result.OffsetY != expOffsetY)
        {
            errors.Add($"[{id}] AnimSample set={set} ticks={ticks}: OffsetY got {result.OffsetY} expected {expOffsetY}");
        }
    }

    // ------------------------------------------------------------------ ClimbSequence verifier
    private static void VerifyClimbSequence(JsonNode c, int id, List<string> errors)
    {
        string startMethod = c["startMethod"]!.GetValue<string>();
        int dir = c["dir"]!.GetValue<int>();
        float x = c["x"]!.GetValue<float>();
        float y = c["y"]!.GetValue<float>();
        int height = c["height"]!.GetValue<int>();
        var inputArr = c["inputs"]!.AsArray();

        var state = new GameState { Position = new Vector2(x, y) };
        var ctrl = new ClimbController();
        ctrl.SetData(StandardData());

        if (startMethod == "Up")
        {
            ctrl.StartClimbUp(state, dir, (int)x, (int)y, height);
        }
        else if (startMethod == "Down")
        {
            ctrl.StartClimbDown(state, dir, (int)x, (int)y, height);
        }
        else
        {
            errors.Add($"[{id}] ClimbSequence unknown startMethod '{startMethod}'");
            return;
        }

        foreach (var inp in inputArr)
        {
            if (!state.ClimbActive)
            {
                break;  // stop when the climb has ended
            }

            ctrl.Tick(state, MakeInput(inp!.GetValue<string>()));
        }

        bool expClimbActive = c["expClimbActive"]!.GetValue<bool>();
        int expClimbHeight = c["expClimbHeight"]!.GetValue<int>();
        int expClimbSide = c["expClimbSide"]!.GetValue<int>();
        int expAnimSet = c["expAnimSet"]!.GetValue<int>();
        int expAnimTicks = c["expAnimTicks"]!.GetValue<int>();
        int expAnimDirection = c["expAnimDirection"]!.GetValue<int>();
        float expPosX = c["expPosX"]!.GetValue<float>();
        float expPosY = c["expPosY"]!.GetValue<float>();
        int expFacing = c["expFacing"]!.GetValue<int>();
        int expCamSmoothing = c["expCameraSmoothing"]!.GetValue<int>();
        bool expOffer = c["expOffer"]!.GetValue<bool>();
        int expStateAnimSet = c["expStateAnimSet"]!.GetValue<int>();
        int expStateAnimTicks = c["expStateAnimTicks"]!.GetValue<int>();

        string tag = $"[{id}] ClimbSequence start={startMethod} dir={dir} h={height}";

        if (state.ClimbActive != expClimbActive)
        {
            errors.Add($"{tag}: ClimbActive got {state.ClimbActive} expected {expClimbActive}");
        }

        if (state.ClimbHeight != expClimbHeight)
        {
            errors.Add($"{tag}: ClimbHeight got {state.ClimbHeight} expected {expClimbHeight}");
        }

        if (state.ClimbSide != expClimbSide)
        {
            errors.Add($"{tag}: ClimbSide got {state.ClimbSide} expected {expClimbSide}");
        }

        if (state.ClimbAnimationSet != expAnimSet)
        {
            errors.Add($"{tag}: ClimbAnimationSet got {state.ClimbAnimationSet} expected {expAnimSet}");
        }

        if (state.ClimbAnimationTicks != expAnimTicks)
        {
            errors.Add($"{tag}: ClimbAnimationTicks got {state.ClimbAnimationTicks} expected {expAnimTicks}");
        }

        if (state.AnimationDirection != expAnimDirection)
        {
            errors.Add($"{tag}: AnimationDirection got {state.AnimationDirection} expected {expAnimDirection}");
        }

        if (state.Position.X != expPosX)
        {
            errors.Add($"{tag}: Position.X got {state.Position.X} expected {expPosX}");
        }

        if (state.Position.Y != expPosY)
        {
            errors.Add($"{tag}: Position.Y got {state.Position.Y} expected {expPosY}");
        }

        if (state.Facing != expFacing)
        {
            errors.Add($"{tag}: Facing got {state.Facing} expected {expFacing}");
        }

        if (state.CameraSmoothingTicks != expCamSmoothing)
        {
            errors.Add($"{tag}: CameraSmoothingTicks got {state.CameraSmoothingTicks} expected {expCamSmoothing}");
        }

        if (state.Offer != expOffer)
        {
            errors.Add($"{tag}: Offer got {state.Offer} expected {expOffer}");
        }

        if (state.AnimationSet != expStateAnimSet)
        {
            errors.Add($"{tag}: AnimationSet got {state.AnimationSet} expected {expStateAnimSet}");
        }

        if (state.AnimationTicks != expStateAnimTicks)
        {
            errors.Add($"{tag}: AnimationTicks got {state.AnimationTicks} expected {expStateAnimTicks}");
        }
    }
}
