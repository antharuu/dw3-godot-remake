using System.Text;
using System.Text.Json.Nodes;
using Xunit;

namespace Dw3.Game.Tests;

/// <summary>
/// Generates tests/Dw3.Game.Tests/Vectors/battle-shot-camera.json when GENERATE_VECTORS=1.
/// The file is written to GENERATE_VECTORS_OUT (or the test output Vectors directory):
///   GENERATE_VECTORS=1 GENERATE_VECTORS_OUT=$PWD/tests/Dw3.Game.Tests/Vectors/battle-shot-camera.json \
///     dotnet test tests/Dw3.Game.Tests --filter "FullyQualifiedName~GenerateBattleShotCameraVectors"
///
/// Every input (RNG values, provider views, operation lists) is fabricated from a fixed seed;
/// the expected outputs are produced by the current BattleShotCamera implementation.
/// </summary>
public sealed class BattleShotCameraVectorGenerator
{
    private const int Seed = 20261031;

    private static readonly int[] RngPool =
    [
        0, 1, 2, 3, 4, 5, 6, 7, 8, 9, -1, -2, -3, -4, -5, 1000, 1001, 1002,
        int.MaxValue, int.MaxValue - 1, int.MaxValue - 2, int.MinValue + 1, int.MinValue + 2,
        123456789, -987654321,
    ];

    private sealed class ViewSource
    {
        private readonly Random _random;
        private readonly bool _extreme;
        public List<int[]> Issued { get; } = new();

        public ViewSource(Random random, bool extreme)
        {
            _random = random;
            _extreme = extreme;
        }

        public int[] Next()
        {
            var v = new int[14];
            for (var i = 0; i < 14; i++) v[i] = Field(i);
            Issued.Add(v);
            return (int[])v.Clone();
        }

        private int Field(int i)
        {
            if (_extreme && _random.Next(3) == 0)
            {
                int[] edges = [int.MaxValue, int.MinValue, int.MaxValue - 1, int.MinValue + 1, int.MaxValue - 0x13FF, int.MinValue + 0x13FF, -1, 0];
                return edges[_random.Next(edges.Length)];
            }
            return i switch
            {
                13 => _random.Next(1, 1024),                 // projection
                10 => _random.Next(-12000, 12000),           // yaw, deliberately outside one turn
                9 or 11 or 12 => _random.Next(0, 4096),
                _ => _random.Next(-30000, 30001),
            };
        }
    }

    private sealed class Builder
    {
        private readonly Random _random;
        public BattleShotCameraVectorHarness.Session Session { get; }
        public int[] Rng { get; }
        public ViewSource? Enemy { get; }
        public ViewSource? Fighter { get; }
        public string Family { get; }

        public Builder(Random random, string family, int[] rng, bool neutral, bool extreme)
        {
            _random = random;
            Family = family;
            Rng = rng;
            if (!neutral)
            {
                Enemy = new ViewSource(new Random(random.Next()), extreme);
                Fighter = new ViewSource(new Random(random.Next()), extreme);
            }
            Session = new BattleShotCameraVectorHarness.Session(rng, Enemy is null ? null : Enemy.Next,
                Fighter is null ? null : Fighter.Next);
        }

        public bool Op(string op) => !Session.Failed && Session.Apply(op);

        /// <summary>Crosses the current shot boundary with single steps on both sides.</summary>
        public void WalkBoundary(int before, int after)
        {
            var bulk = Session.Camera.Time - before;
            if (bulk > 0 && !Op($"s:{bulk}")) return;
            for (var i = 0; i < before + after; i++)
                if (!Op("1")) return;
        }

        public JsonObject ToJson(int id)
        {
            var r = Session.ToResult();
            var obj = new JsonObject
            {
                ["id"] = id,
                ["family"] = Family,
                ["ctor"] = Enemy is null ? "neutral" : "views",
                ["rng"] = Arr(Rng),
                ["enemyViews"] = Rows(Enemy?.Issued ?? []),
                ["fighterViews"] = Rows(Fighter?.Issued ?? []),
                ["ops"] = new JsonArray(Session.Ops.Select(o => (JsonNode)o!).ToArray()),
                ["snapshots"] = Rows(r.Snapshots),
                ["calls"] = new JsonArray(r.Calls.Select(c =>
                    (JsonNode)new JsonArray(c.Select(s => (JsonNode)s!).ToArray())).ToArray()),
                ["throwsAt"] = r.ThrowsAt,
                ["exception"] = r.Exception,
            };
            return obj;
        }

        public int RandomInt(int min, int max) => _random.Next(min, max);
    }

    private static JsonArray Arr(int[] a) => new(a.Select(v => (JsonNode)v).ToArray());
    private static JsonArray Rows(IEnumerable<int[]> rows) => new(rows.Select(r => (JsonNode)Arr(r)).ToArray());

    private static int[] RandomRng(Random random, int maxLength = 5)
    {
        var n = random.Next(1, maxLength + 1);
        var v = new int[n];
        for (var i = 0; i < n; i++)
            v[i] = random.Next(4) == 0 ? random.Next(int.MinValue + 1, int.MaxValue) : RngPool[random.Next(RngPool.Length)];
        return v;
    }

    [Fact]
    public void GenerateBattleShotCameraVectors()
    {
        if (Environment.GetEnvironmentVariable("GENERATE_VECTORS") != "1") return;

        var random = new Random(Seed);
        var cases = new List<JsonObject>();
        void Add(Builder b) => cases.Add(b.ToJson(cases.Count + 1));

        // 1. Boundary walks through 3..7 shots, fabricated views (some neutral).
        for (var n = 0; n < 150; n++)
        {
            var b = new Builder(random, "boundary-walk", RandomRng(random), neutral: n % 10 == 0, extreme: false);
            var shots = random.Next(3, 8);
            for (var s = 0; s < shots && !b.Session.Failed; s++) b.WalkBoundary(2, 2);
            Add(b);
        }

        // 2. Single-frame traces inside a shot (first frames and mid-shot) to pin per-frame rules.
        for (var n = 0; n < 40; n++)
        {
            var b = new Builder(random, "per-frame", RandomRng(random), neutral: n % 8 == 0, extreme: false);
            var skipShots = random.Next(0, 9);
            for (var s = 0; s < skipShots && !b.Session.Failed; s++) b.Op($"s:{b.Session.Camera.Time}");
            var offset = random.Next(0, 3) == 0 ? 0 : b.RandomInt(0, Math.Max(1, b.Session.Camera.Time - 12));
            if (offset > 0) b.Op($"s:{offset}");
            for (var i = 0; i < 12; i++) b.Op("1");
            Add(b);
        }

        // 3. Random interleavings of Step, StepFrames (including <= 0) and Stop.
        for (var n = 0; n < 70; n++)
        {
            var b = new Builder(random, "stop-interleave", RandomRng(random), neutral: n % 7 == 0, extreme: false);
            var count = random.Next(8, 25);
            for (var i = 0; i < count && !b.Session.Failed; i++)
            {
                var roll = random.Next(10);
                var op = roll switch
                {
                    0 or 1 => "x",
                    2 or 3 or 4 => "1",
                    5 => $"s:{random.Next(-5, 1)}",
                    6 => $"s:{Math.Max(1, b.Session.Camera.Time - 1)}",
                    7 => $"s:{b.Session.Camera.Time}",
                    _ => $"s:{random.Next(1, 700)}",
                };
                b.Op(op);
            }
            Add(b);
        }

        // 4. Extreme provider values: 32-bit wraparound of offsets and yaw.
        for (var n = 0; n < 40; n++)
        {
            var b = new Builder(random, "extreme-values", RandomRng(random), neutral: false, extreme: true);
            var shots = random.Next(2, 7);
            for (var s = 0; s < shots && !b.Session.Failed; s++)
            {
                b.WalkBoundary(1, 2);
                if (random.Next(4) == 0) b.Op("x");
            }
            Add(b);
        }

        // 5. RNG edge values, including the one value whose magnitude is not representable.
        int[][] edgeRngs =
        [
            [int.MinValue], [0, int.MinValue], [1, 2, int.MinValue], [int.MinValue + 1], [int.MaxValue],
            [-1], [-2], [-3], [3], [6], [-6], [7, -7], [int.MaxValue, int.MinValue + 1],
            [2, 2, 2], [1, 1, 1], [0, 0, 0], [0, 1, 2, 0, 1, 2], [-4, -5, -6], [1002, 1001, 1000],
            [int.MaxValue - 1, -2147483646],
        ];
        foreach (var rng in edgeRngs)
        {
            foreach (var neutral in new[] { true, false })
            {
                var b = new Builder(random, "rng-edge", rng, neutral, extreme: false);
                // Opening list (two shots), then whole lists' worth of shots.
                for (var s = 0; s < 9 && !b.Session.Failed; s++) b.Op($"s:{b.Session.Camera.Time}");
                Add(b);
            }
        }

        var sb = new StringBuilder();
        sb.Append("{\n  \"metadata\": ");
        sb.Append(new JsonObject
        {
            ["seed"] = Seed,
            ["count"] = cases.Count,
            ["note"] = "Fabricated vectors generated from the current BattleShotCamera implementation. All inputs are invented; no game values are embedded.",
            ["snapshotLayout"] = "List, Shot, Time, Substate, PlayerVisible, EnemyVisible, Vpx, Vpy, Vpz, Vrx, Vry, Vrz, Tx, Ty, Tz, RotationX, RotationY, RotationZ, Rz, Projection",
            ["ops"] = "\"1\" = Step(), \"s:N\" = StepFrames(N), \"x\" = Stop()",
            ["calls"] = "\"E\" = enemy view provider, \"F{slot}:{index}\" = fighter view provider",
        }.ToJsonString());
        sb.Append(",\n  \"cases\": [\n");
        for (var i = 0; i < cases.Count; i++)
        {
            sb.Append("    ").Append(cases[i].ToJsonString());
            sb.Append(i + 1 < cases.Count ? ",\n" : "\n");
        }
        sb.Append("  ]\n}\n");

        var outPath = Environment.GetEnvironmentVariable("GENERATE_VECTORS_OUT") is { Length: > 0 } p
            ? p
            : Path.Combine(AppContext.BaseDirectory, "Vectors", "battle-shot-camera.json");
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        File.WriteAllText(outPath, sb.ToString());

        Assert.True(cases.Count >= 300, $"Generated {cases.Count} vectors, need at least 300.");
    }
}
