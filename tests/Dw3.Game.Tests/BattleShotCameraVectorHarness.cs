using System.Text.Json.Nodes;
using Dw3Production.Presentation;

namespace Dw3.Game.Tests;

/// <summary>
/// Shared driver for the battle-shot-camera differential vectors.
///
/// A scenario is a constructor choice, an RNG value list (cycled), view providers and a list
/// of operations. Running it records, after every operation, a 20-integer snapshot of the
/// public camera state plus the provider calls made during that operation. All inputs are
/// fabricated; no game values are involved.
///
/// Snapshot layout: List, Shot, Time, Substate, PlayerVisible (0/1), EnemyVisible (0/1),
/// then the 14 view fields in <see cref="ViewFields"/> order.
/// Operation encoding: "1" = Step(), "s:N" = StepFrames(N), "x" = Stop().
/// Provider call encoding: "E" = enemy view, "F{slot}:{index}" = fighter view.
/// </summary>
internal static class BattleShotCameraVectorHarness
{
    public static readonly string[] ViewFields =
    [
        "Vpx", "Vpy", "Vpz", "Vrx", "Vry", "Vrz", "Tx", "Ty", "Tz",
        "RotationX", "RotationY", "RotationZ", "Rz", "Projection",
    ];

    public static SourceBattleCameraView ToView(int[] f) => new()
    {
        Vpx = f[0],
        Vpy = f[1],
        Vpz = f[2],
        Vrx = f[3],
        Vry = f[4],
        Vrz = f[5],
        Tx = f[6],
        Ty = f[7],
        Tz = f[8],
        RotationX = f[9],
        RotationY = f[10],
        RotationZ = f[11],
        Rz = f[12],
        Projection = f[13],
    };

    public static int[] FromView(SourceBattleCameraView v) =>
    [
        v.Vpx, v.Vpy, v.Vpz, v.Vrx, v.Vry, v.Vrz, v.Tx, v.Ty, v.Tz,
        v.RotationX, v.RotationY, v.RotationZ, v.Rz, v.Projection,
    ];

    public static Result Run(
        int[] rngValues,
        Func<int[]>? nextEnemyView,
        Func<int[]>? nextFighterView,
        IReadOnlyList<string> ops)
    {
        var session = new Session(rngValues, nextEnemyView, nextFighterView);
        foreach (var op in ops)
        {
            if (!session.Apply(op))
            {
                break;
            }
        }

        return session.ToResult();
    }

    public static int[] Snapshot(BattleShotCamera c)
    {
        var s = new int[20];
        s[0] = c.List;
        s[1] = c.Shot;
        s[2] = c.Time;
        s[3] = c.Substate;
        s[4] = c.PlayerVisible ? 1 : 0;
        s[5] = c.EnemyVisible ? 1 : 0;
        FromView(c.View).CopyTo(s, 6);
        return s;
    }

    public static int[] Ints(JsonNode? node)
    {
        var a = node!.AsArray();
        var r = new int[a.Count];
        for (var i = 0; i < a.Count; i++)
        {
            r[i] = a[i]!.GetValue<int>();
        }

        return r;
    }

    public sealed class CycledRng : IShotCameraRng
    {
        private readonly int[] _values;
        private int _index;

        public CycledRng(int[] values)
        {
            if (values.Length == 0)
            {
                throw new ArgumentException("RNG value list must not be empty");
            }

            _values = values;
        }

        public int NextRoll() => _values[_index++ % _values.Length];
    }

    public sealed record Result(List<int[]> Snapshots, List<List<string>> Calls, int ThrowsAt, string? Exception);

    /// <summary>
    /// A live recording session. <c>nextEnemyView</c> / <c>nextFighterView</c>
    /// are null for the neutral (two-argument) constructor; otherwise they supply successive fresh
    /// views. Snapshot 0 is the state right after construction. After the first exception the
    /// session refuses further operations.
    /// </summary>
    public sealed class Session
    {
        private List<string> _current = [];

        public Session(int[] rngValues, Func<int[]>? nextEnemyView, Func<int[]>? nextFighterView)
        {
            var rng = new CycledRng(rngValues);
            if (nextEnemyView is null || nextFighterView is null)
            {
                Camera = new BattleShotCamera(rng, enemyCameraCount: 3);
            }
            else
            {
                Camera = new BattleShotCamera(rng, new BattleShotCameraViews
                {
                    GetEnemyView = () =>
                    {
                        _current.Add("E");
                        return ToView(nextEnemyView());
                    },
                    GetFighterView = (slot, index) =>
                    {
                        _current.Add($"F{slot}:{index}");
                        return ToView(nextFighterView());
                    },
                });
            }

            Snapshots.Add(Snapshot(Camera));
            Calls.Add(_current);
        }

        public BattleShotCamera Camera { get; }

        public List<string> Ops { get; } = [];

        public List<int[]> Snapshots { get; } = [];

        public List<List<string>> Calls { get; } = [];

        public int ThrowsAt { get; private set; } = -1;

        public string? Exception { get; private set; }

        public bool Failed => Exception is not null;

        /// <summary>Applies one operation and records its snapshot. Returns false once failed.</summary>
        public bool Apply(string op)
        {
            if (Failed)
            {
                throw new InvalidOperationException("session already failed");
            }

            Ops.Add(op);
            _current = [];
            try
            {
                ApplyOp(Camera, op);
            }
            catch (Exception ex)
            {
                Calls.Add(_current);
                ThrowsAt = Ops.Count - 1;
                Exception = ex.GetType().Name;
                return false;
            }

            Snapshots.Add(Snapshot(Camera));
            Calls.Add(_current);
            return true;
        }

        public Result ToResult() => new(Snapshots, Calls, ThrowsAt, Exception);

        private static void ApplyOp(BattleShotCamera camera, string op)
        {
            if (op == "1")
            {
                camera.Step();
            }
            else if (op == "x")
            {
                camera.Stop();
            }
            else if (op.StartsWith("s:", StringComparison.Ordinal))
            {
                camera.StepFrames(int.Parse(op[2..], System.Globalization.CultureInfo.InvariantCulture));
            }
            else
            {
                throw new InvalidOperationException($"unknown vector op '{op}'");
            }
        }
    }
}
