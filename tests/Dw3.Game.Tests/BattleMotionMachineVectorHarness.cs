using System.Text.Json.Nodes;
using Dw3Production.Presentation;

namespace Dw3.Game.Tests;

/// <summary>
/// Shared driver for the battle-motion-machine differential vectors.
///
/// A scenario is a fabricated motion table (motions → steps → four integers), a constructor
/// idle argument and a list of operations. Running it records, after the constructor and after
/// every operation, an outcome code and (unless the scenario stopped) an 11-integer snapshot of
/// the public state plus the keyframe list when it changed. All inputs are fabricated; no game
/// values are involved.
///
/// Snapshot layout: Motion, IdleMotion, Keyframe, Frame, Blend, Blending (0/1),
/// MotionDone (0/1), ToIdle (0/1), BlendTarget, BlendSource, Looping (0/1).
/// Operation encoding: "s:N" = Step(N), "m:M:R" = SetMotion(M, restart: R == 1),
/// "i:K" = SetIdleMotion(K).
/// Outcome codes: "ok"; "V:param" = argument validation failure (ArgumentOutOfRangeException
/// with that parameter name, state unchanged, scenario continues); "E:type" = failure that
/// ends the scenario with that exact exception type; "E:*" = failure that ends the scenario
/// with any exception type (malformed motion data).
/// </summary>
internal static class BattleMotionMachineVectorHarness
{
    public sealed record Result(List<string> Outcomes, List<int[]?> Snapshots, List<int[]?> Keyframes);

    public static int[] Snapshot(BattleMotionMachine m) =>
    [
        m.Motion, m.IdleMotion, m.Keyframe, m.Frame, m.Blend, B(m.Blending), B(m.MotionDone),
        B(m.ToIdle), m.BlendTarget, m.BlendSource, B(m.Looping),
    ];

    private static int B(bool v) => v ? 1 : 0;

    public static int[][][] Motions(JsonNode? node) =>
        node!.AsArray().Select(m => m!.AsArray().Select(s => Ints(s)).ToArray()).ToArray();

    public static int[] Ints(JsonNode? node) => node!.AsArray().Select(v => v!.GetValue<int>()).ToArray();

    public static Result Run(int[][][] motions, int idle, IReadOnlyList<string> ops)
    {
        var outcomes = new List<string>();
        var snapshots = new List<int[]?>();
        var keyframes = new List<int[]?>();
        BattleMotionMachine machine;
        try
        {
            machine = new BattleMotionMachine(motions, idle);
        }
        catch (Exception ex)
        {
            outcomes.Add(StopCode(ex));
            snapshots.Add(null);
            keyframes.Add(null);
            return new Result(outcomes, snapshots, keyframes);
        }
        outcomes.Add("ok");
        snapshots.Add(Snapshot(machine));
        var last = machine.Keyframes.ToArray();
        keyframes.Add(last);

        foreach (var op in ops)
        {
            string code;
            try
            {
                Apply(machine, op);
                code = "ok";
            }
            catch (ArgumentOutOfRangeException ex) when (IsValidation(op, ex))
            {
                code = "V:" + ex.ParamName;
            }
            catch (Exception ex)
            {
                outcomes.Add(StopCode(ex));
                snapshots.Add(null);
                keyframes.Add(null);
                break;
            }
            outcomes.Add(code);
            snapshots.Add(Snapshot(machine));
            var now = machine.Keyframes.ToArray();
            keyframes.Add(now.SequenceEqual(last) ? null : now);
            last = now;
        }
        return new Result(outcomes, snapshots, keyframes);
    }

    private static void Apply(BattleMotionMachine m, string op)
    {
        var p = op.Split(':');
        switch (p[0])
        {
            case "s": m.Step(int.Parse(p[1])); break;
            case "m": m.SetMotion(int.Parse(p[1]), p[2] == "1"); break;
            case "i": m.SetIdleMotion(int.Parse(p[1])); break;
            default: throw new FormatException($"unknown vector op '{op}'");
        }
    }

    /// <summary>Argument checks performed before any state change, per operation kind.</summary>
    private static bool IsValidation(string op, ArgumentOutOfRangeException ex) => op[0] switch
    {
        's' => ex.ParamName == "framesElapsed",
        'm' => ex.ParamName == "motion",
        'i' => ex.ParamName == "idleMotion",
        _ => false,
    };

    private static string StopCode(Exception ex) => ex switch
    {
        InvalidOperationException => "E:InvalidOperationException",
        ArgumentOutOfRangeException { ParamName: "motion" } => "E:ArgumentOutOfRangeException",
        _ => "E:*",
    };
}
