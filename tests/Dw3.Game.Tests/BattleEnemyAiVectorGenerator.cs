using System.Text.Json;
using System.Text.Json.Nodes;
using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests;

/// <summary>
/// Regenerates Vectors/battle-enemy-ai.json from the current BattleEnemyAi implementation.
/// Disabled unless GENERATE_VECTORS=1; the file is written to GENERATE_VECTORS_OUT (or the
/// test output directory when unset). Run via:
///   GENERATE_VECTORS=1 GENERATE_VECTORS_OUT=$PWD/tests/Dw3.Game.Tests/Vectors/battle-enemy-ai.json \
///     dotnet test --filter "GenerateBattleEnemyAiVectors"
/// Every input is invented (fixed seed, boundaries, edge cases); no game value is embedded.
/// </summary>
public sealed class BattleEnemyAiVectorGenerator
{
    private const int Seed = 33;

    private readonly JsonArray _cases = [];

    private readonly Random _random = new(Seed);

    [Fact]
    public void GenerateBattleEnemyAiVectors()
    {
        if (Environment.GetEnvironmentVariable("GENERATE_VECTORS") != "1")
        {
            return;
        }

        ConditionCases();
        ActionKindCases();
        PickActionCases();
        PickCounterCases();
        ResolveSwitchCases();

        var root = new JsonObject
        {
            ["metadata"] = new JsonObject
            {
                ["seed"] = Seed,
                ["count"] = _cases.Count,
                ["note"] = "Fabricated vectors generated from the current BattleEnemyAi implementation. " +
                           "All inputs are invented; no game values are embedded. 'rng' is the scripted " +
                           "random sequence, 'expected.rngCalls' how many values were drawn.",
            },
            ["cases"] = _cases,
        };
        var outPath = Environment.GetEnvironmentVariable("GENERATE_VECTORS_OUT") is { Length: > 0 } p
            ? p
            : Path.Combine(AppContext.BaseDirectory, "Vectors", "battle-enemy-ai.json");
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        File.WriteAllText(outPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }) + "\n");

        Assert.True(_cases.Count >= 300, $"Generated {_cases.Count} vectors, need at least 300.");
    }

    private static BattleFighter Enemy(
        EnemyStats? stats = null,
        int[]? boosts = null,
        int[]? techs = null,
        BattleTableAction[]? actions = null,
        BattleTableAction counter = default)
    {
        var values = stats ?? new EnemyStats();
        return new BattleFighter
        {
            Hp = values.Hp,
            MaxHp = values.MaxHp,
            Mp = values.Mp,
            Charge = values.Charge,
            Turns = values.Turns,
            Boosts = boosts ?? [0, 0, 0, 0],
            Techs = techs ?? [101, 102, 103],
            Actions = actions ?? [],
            Counter = counter,
        };
    }

    private static BattleFighter Slot(int id, int hp, int maxHp = 100, int flags = 0) =>
        new() { Id = id, Hp = hp, MaxHp = maxHp, Flags = flags };

    private static EnemyAiContext Ctx(
        BattleFighter? enemy = null,
        BattleFighter? player = null,
        BattleFighter?[]? enemies = null,
        int activeEnemy = 0,
        (int BoostElement, int BattleId, int EncounterKind) encounter = default,
        int[]? starters = null) => new()
        {
            Enemy = enemy ?? Enemy(),
            ActivePlayer = player,
            Enemies = enemies ?? [],
            ActiveEnemy = activeEnemy,
            BoostElement = encounter.BoostElement,
            BattleId = encounter.BattleId,
            EncounterKind = encounter.EncounterKind,
            StarterDigimonIds = starters ?? [],
        };

    private static int HpShareForTest(int maxHp, int arg) => maxHp / 100 * (arg * 100 / 128);

    private static BattleTableAction A(int target, int condition, int arg = 0) => new(target, condition, arg);

    private void Add(JsonObject input, string note)
    {
        input["id"] = _cases.Count + 1;
        input["note"] = note;
        input["expected"] = BattleEnemyAiVectorCodec.Evaluate(input);
        _cases.Add(input);
    }

    private void Condition(int condition, int arg, EnemyAiContext ctx, int[] rng, string note) => Add(
        new JsonObject
        {
            ["fn"] = "TestEnemyCondition",
            ["condition"] = condition,
            ["arg"] = arg,
            ["ctx"] = BattleEnemyAiVectorCodec.ContextToJson(ctx),
            ["rng"] = BattleEnemyAiVectorCodec.ToJson(rng),
        },
        note);

    private void Pick(string fn, EnemyAiContext ctx, int[] rng, string note) => Add(
        new JsonObject
        {
            ["fn"] = fn,
            ["ctx"] = BattleEnemyAiVectorCodec.ContextToJson(ctx),
            ["rng"] = BattleEnemyAiVectorCodec.ToJson(rng),
        },
        note);

    private void Switch(EnemyDecision d, BattleFighter?[] enemies, int active, int[] rng, string note) => Add(
        new JsonObject
        {
            ["fn"] = "ResolveSwitch",
            ["decision"] = BattleEnemyAiVectorCodec.DecisionToJson(d),
            ["enemies"] = new JsonArray([.. enemies.Select(BattleEnemyAiVectorCodec.SlotToJson)]),
            ["activeEnemy"] = active,
            ["rng"] = BattleEnemyAiVectorCodec.ToJson(rng),
        },
        note);

    private void ConditionCases()
    {
        // Always-true conditions: 0 and anything outside 1..18.
        foreach (int cond in new[] { 0, -1, -128, 19, 20, 64, 255 })
        {
            Condition(cond, 7, Ctx(), [5], $"condition {cond} always holds");
        }

        // 1: random roll, remainder mod 128 compared with the argument.
        foreach (int arg in new[] { 0, 1, 64, 127, 128, 200 })
        {
            foreach (int roll in new[] { 0, 63, 64, 127, 128, 255, 32767, -5 })
            {
                Condition(1, arg, Ctx(), [roll, 9], $"chance arg {arg} roll {roll}");
            }
        }

        // 2/3: the enemy's HP against the HP share.
        foreach (int maxHp in new[] { 0, 99, 100, 250, 999, 5000 })
        {
            foreach (int arg in new[] { 0, 1, 64, 128, 255 })
            {
                int share = HpShareForTest(maxHp, arg);
                foreach (int hp in new[] { share - 1, share, share + 1 }.Distinct())
                {
                    Condition(2, arg, Ctx(enemy: Enemy(stats: new EnemyStats(Hp: hp, MaxHp: maxHp))), [], $"own hp {hp}/{maxHp} below arg {arg}");
                    Condition(3, arg, Ctx(enemy: Enemy(stats: new EnemyStats(Hp: hp, MaxHp: maxHp))), [], $"own hp {hp}/{maxHp} at or above arg {arg}");
                }
            }
        }

        // 4/5: the enemy's MP against the argument.
        foreach (int mp in new[] { 0, 9, 10, 11 })
        {
            Condition(4, 10, Ctx(enemy: Enemy(stats: new EnemyStats(Mp: mp))), [], $"mp {mp} below 10");
            Condition(5, 10, Ctx(enemy: Enemy(stats: new EnemyStats(Mp: mp))), [], $"mp {mp} at or above 10");
        }

        // 6/7: the active player's HP share, including no active player.
        foreach (int cond in new[] { 6, 7 })
        {
            Condition(cond, 64, Ctx(player: null), [], $"cond {cond} without active player");
            foreach (int hp in new[] { 0, 199, 200, 201, 400 })
            {
                Condition(cond, 64, Ctx(player: Slot(12, hp, 400)), [], $"cond {cond} player hp {hp}/400");
            }
        }

        // 8: the active player is one of the starter Digimon.
        Condition(8, 0, Ctx(player: null, starters: [1, 2, 3]), [], "starter check without player");
        Condition(8, 0, Ctx(player: Slot(2, 10), starters: [1, 2, 3]), [], "player is a starter");
        Condition(8, 0, Ctx(player: Slot(4, 10), starters: [1, 2, 3]), [], "player not a starter");
        Condition(8, 0, Ctx(player: Slot(2, 10), starters: []), [], "empty starter list");
        Condition(8, 99, Ctx(player: Slot(0, 0), starters: [0]), [], "starter id 0, arg ignored");

        // 9: the active player is asleep.
        foreach (int flags in new[] { 0, FighterFlags.Asleep, FighterFlags.Poisoned, FighterFlags.Asleep | FighterFlags.Confused, 0x7F })
        {
            Condition(9, 0, Ctx(player: Slot(5, 10, flags: flags)), [], $"player flags {flags}");
        }

        Condition(9, 0, Ctx(player: null), [], "asleep check without player");

        // 10: another enemy is alive (any, or with a given id).
        var rosters = new (BattleFighter?[] Roster, int Active, string Label)[]
        {
            ([Slot(10, 5), Slot(11, 5), Slot(12, 5)], 0, "all alive, active 0"),
            ([Slot(10, 5), Slot(11, 0), Slot(12, 0)], 0, "others down"),
            ([Slot(10, 5), null, Slot(12, 3)], 0, "middle empty"),
            ([Slot(10, 5), Slot(0, 9), null], 0, "other id 0"),
            ([Slot(10, 5), Slot(11, -3), null], 0, "other negative hp"),
            ([Slot(10, 5)], 0, "single entry"),
            ([], 0, "empty roster"),
            ([Slot(10, 5), Slot(11, 5), Slot(12, 5), Slot(13, 5)], 0, "fourth entry ignored"),
            ([Slot(10, 0), Slot(11, 0), Slot(12, 0), Slot(13, 5)], 1, "only the fourth alive"),
            ([Slot(10, 5), Slot(11, 5), Slot(12, 0)], 1, "active 1"),
            ([Slot(10, 5), Slot(11, 5), Slot(12, 5)], -1, "active out of range low"),
            ([Slot(10, 5), Slot(11, 5), Slot(12, 5)], 7, "active out of range high"),
        };
        foreach (var (roster, active, label) in rosters)
            foreach (int arg in new[] { 0, 11, 12, 13 })
            {
                Condition(10, arg, Ctx(enemies: roster, activeEnemy: active), [], $"other enemy {label}, arg {arg}");
            }

        // 11/12/13: equality with the battle's element boost, battle id and encounter kind.
        foreach (int value in new[] { 0, 3, 4 })
        {
            Condition(11, 3, Ctx(encounter: (value, 0, 0)), [], $"boost element {value} vs 3");
            Condition(12, 3, Ctx(encounter: (0, value, 0)), [], $"battle id {value} vs 3");
            Condition(13, 3, Ctx(encounter: (0, 0, value)), [], $"encounter kind {value} vs 3");
        }

        // 14: the enemy holds a charge.
        foreach (int charge in new[] { 0, 1, 32, -4 })
        {
            Condition(14, 0, Ctx(enemy: Enemy(stats: new EnemyStats(Charge: charge))), [], $"charge {charge}");
        }

        // 15/16/17: one of the first three boosts is lowered.
        foreach (int cond in new[] { 15, 16, 17 })
        {
            foreach (int value in new[] { -1, 0, 1 })
            {
                var boosts = new[] { 0, 0, 0, -9 };
                boosts[cond - 15] = value;
                Condition(cond, 0, Ctx(enemy: Enemy(boosts: boosts)), [], $"cond {cond} boost {value}");
            }
        }

        Condition(17, 0, Ctx(enemy: Enemy(boosts: [0, 0])), [], "boost array too short");

        // 18: the enemy's turn count is a multiple of the argument.
        foreach (int turns in new[] { 0, 1, 4, 7, -4 })
        {
            foreach (int arg in new[] { 0, 1, 2, 3, -2 })
            {
                Condition(18, arg, Ctx(enemy: Enemy(stats: new EnemyStats(Turns: turns))), [], $"turns {turns} every {arg}");
            }
        }
    }

    private void ActionKindCases()
    {
        foreach (int kind in Enumerable.Range(-1, 12))
        {
            Add(
                new JsonObject
                {
                    ["fn"] = "GetEnemyAction",
                    ["enemy"] = BattleEnemyAiVectorCodec.EnemyToJson(Enemy(techs: [201, 202, 203])),
                    ["targetKind"] = kind,
                    ["rng"] = new JsonArray(),
                },
                $"target kind {kind}");
        }

        foreach (int kind in new[] { 1, 3, 4 })
        {
            Add(
                new JsonObject
                {
                    ["fn"] = "GetEnemyAction",
                    ["enemy"] = BattleEnemyAiVectorCodec.EnemyToJson(Enemy(techs: [301, 302])),
                    ["targetKind"] = kind,
                    ["rng"] = new JsonArray(),
                },
                $"target kind {kind} with two techniques");
        }

        foreach (EnemyActionKind kind in Enum.GetValues<EnemyActionKind>())
        {
            Add(
                new JsonObject
                {
                    ["fn"] = "DecisionFlags",
                    ["decision"] = BattleEnemyAiVectorCodec.DecisionToJson(new EnemyDecision(kind, 0, -1)),
                    ["rng"] = new JsonArray(),
                },
                $"flags of {kind}");
        }
    }

    private void PickActionCases()
    {
        Pick("PickAction", Ctx(enemy: Enemy(actions: [])), [], "empty table");
        Pick("PickAction", Ctx(enemy: Enemy(actions: [A(2, 99)])), [], "single action is the fallback");
        Pick("PickAction", Ctx(enemy: Enemy(actions: [A(4, 1, 0), A(3, 0)])), [10], "two actions, first fails");
        Pick("PickAction", Ctx(enemy: Enemy(actions: [A(4, 1, 128), A(3, 0)])), [10], "two actions, first holds");
        Pick("PickAction", Ctx(enemy: Enemy(actions: [A(2, 1, 0), A(3, 1, 0), A(4, 0)])), [0, 0], "three actions, fallback");
        Pick("PickAction", Ctx(enemy: Enemy(actions: [A(2, 1, 0), A(3, 1, 0), A(4, 1, 0), A(1, 1, 0)])), [0, 0, 0], "four, all fail, fallback untested");
        Pick("PickAction", Ctx(enemy: Enemy(actions: [A(2, 1, 0), A(3, 1, 0), A(4, 1, 0), A(1, 0), A(5, 0)])), [0, 0, 0], "fifth action ignored");
        Pick("PickAction", Ctx(enemy: Enemy(actions: [A(2, 1, 10), A(3, 1, 10), A(4, 1, 10), A(1, 0)])), [50, 5, 99], "second roll holds, third not drawn");
        Pick("PickAction", Ctx(enemy: Enemy(stats: new EnemyStats(Hp: 10, MaxHp: 1000), actions: [A(3, 2, 32), A(2, 0), A(1, 0), A(4, 0)])), [], "low hp picks technique");
        Pick("PickAction", Ctx(enemy: Enemy(stats: new EnemyStats(Hp: 900, MaxHp: 1000), actions: [A(3, 2, 32), A(2, 0), A(1, 0), A(4, 0)])), [], "high hp falls to second");
        Pick("PickAction", Ctx(enemy: Enemy(actions: [A(9, 0), A(1, 0), A(1, 0), A(1, 0)])), [], "first holds with unknown kind");
        Pick(
            "PickAction",
            Ctx(
            enemy: Enemy(actions: [A(8, 10, 0), A(1, 0), A(1, 0), A(1, 0)]),
            enemies: [Slot(10, 5), Slot(11, 5), null]),
            [],
            "switch when another enemy is alive");
        Pick(
            "PickAction",
            Ctx(
            enemy: Enemy(actions: [A(8, 10, 0), A(4, 0), A(1, 0), A(6, 0)]),
            enemies: [Slot(10, 5), null, null]),
            [],
            "no other enemy, second holds");

        // Seeded random tables over every condition and target kind.
        for (int i = 0; i < 60; i++)
        {
            int count = _random.Next(0, 6);
            var actions = new BattleTableAction[count];
            for (int k = 0; k < count; k++)
            {
                actions[k] = A(_random.Next(-1, 10), _random.Next(0, 20), _random.Next(0, 160));
            }

            var ctx = RandomContext(actions, default);
            Pick("PickAction", ctx, RandomRolls(4), $"seeded table {i}");
        }
    }

    private void PickCounterCases()
    {
        Pick("PickCounter", Ctx(enemy: Enemy(counter: A(1, 0))), [], "condition 0 is no counter");
        Pick("PickCounter", Ctx(enemy: Enemy(counter: A(1, 1, 128))), [3], "attack counter holds");
        Pick("PickCounter", Ctx(enemy: Enemy(counter: A(1, 1, 0))), [3], "attack counter fails");
        foreach (int target in Enumerable.Range(-1, 11))
        {
            Pick("PickCounter", Ctx(enemy: Enemy(stats: new EnemyStats(Charge: 1), counter: A(target, 14))), [], $"counter target kind {target}");
        }

        Pick("PickCounter", Ctx(enemy: Enemy(stats: new EnemyStats(Charge: 0), counter: A(2, 14))), [], "technique counter without charge");
        Pick("PickCounter", Ctx(enemy: Enemy(counter: A(2, 25))), [], "unknown condition counter holds");
        for (int i = 0; i < 40; i++)
        {
            var counter = A(_random.Next(-1, 10), _random.Next(0, 20), _random.Next(0, 160));
            Pick("PickCounter", RandomContext([], counter), RandomRolls(2), $"seeded counter {i}");
        }
    }

    private void ResolveSwitchCases()
    {
        BattleFighter?[] full = [Slot(10, 5), Slot(11, 5), Slot(12, 5)];
        foreach (int slot in new[] { 0, 1, 2 })
        {
            foreach (int active in new[] { 0, 1, 2 })
            {
                Switch(new EnemyDecision(EnemyActionKind.SwitchTo0 + slot, 0, slot), full, active, [], $"fixed slot {slot} active {active}");
            }
        }

        Switch(new EnemyDecision(EnemyActionKind.SwitchTo1, 0, 1), [Slot(10, 5), Slot(11, 0), Slot(12, 5)], 0, [], "fixed slot knocked out");
        Switch(new EnemyDecision(EnemyActionKind.SwitchTo1, 0, 1), [Slot(10, 5), Slot(11, -2), Slot(12, 5)], 0, [], "fixed slot negative hp");
        Switch(new EnemyDecision(EnemyActionKind.SwitchTo1, 0, 1), [Slot(10, 5), Slot(0, 5), Slot(12, 5)], 0, [], "fixed slot id 0");
        Switch(new EnemyDecision(EnemyActionKind.SwitchTo2, 0, 2), [Slot(10, 5), Slot(11, 5), null], 0, [], "fixed slot empty");
        Switch(new EnemyDecision(EnemyActionKind.SwitchTo2, 0, 2), [Slot(10, 5), Slot(11, 5)], 0, [], "fixed slot beyond roster");
        Switch(new EnemyDecision(EnemyActionKind.SwitchTo0, 0, 5), full, 0, [], "slot 5 beyond roster");

        var any = new EnemyDecision(EnemyActionKind.SwitchToAny, 0, -1);
        foreach (int roll in new[] { 0, 1, 2, 3, 0x7FFF, -1, -2 })
        {
            foreach (int active in new[] { 0, 1, 2 })
            {
                Switch(any, full, active, [roll], $"any, two candidates, active {active} roll {roll}");
            }
        }

        Switch(any, [Slot(10, 5), Slot(11, 5), Slot(12, 0)], 0, [7], "any, one candidate");
        Switch(any, [Slot(10, 5), null, Slot(0, 5)], 0, [7], "any, no candidate");
        Switch(any, [Slot(10, 5), Slot(11, 5)], 0, [7], "any, two-entry roster");
        Switch(any, [], 0, [7], "any, empty roster");
        Switch(any, [Slot(10, 5), Slot(11, 5), Slot(12, 5), Slot(13, 5)], 0, [1], "any, fourth entry ignored");
        foreach (int roll in new[] { 0, 1, 2 })
        {
            Switch(any, full, -1, [roll], $"any, active outside roster, roll {roll}");
        }

        Switch(EnemyDecision.None, full, 0, [1], "none decision resolves as any");
        Switch(new EnemyDecision(EnemyActionKind.Attack, 101, -1), full, 2, [0], "attack decision resolves as any");
        Switch(new EnemyDecision(EnemyActionKind.SwitchToAny, 0, -7), full, 1, [1], "negative slot resolves as any");

        for (int i = 0; i < 30; i++)
        {
            int count = _random.Next(0, 5);
            var roster = new BattleFighter?[count];
            for (int k = 0; k < count; k++)
            {
                roster[k] = _random.Next(5) == 0 ? null : RandomSlot(20 + k, -2, 4);
            }

            int slot = _random.Next(-1, Math.Max(count, 1));
            var kind = slot < 0 ? EnemyActionKind.SwitchToAny : EnemyActionKind.SwitchTo0 + Math.Min(slot, 2);
            Switch(new EnemyDecision(kind, 0, slot), roster, _random.Next(-1, 4), RandomRolls(1), $"seeded switch {i}");
        }
    }

    // Draws, in this order, whether the slot id is replaced by 0 (one chance in three), then the hp.
    private BattleFighter RandomSlot(int id, int minHp, int maxHpExclusive)
    {
        int slotId = _random.Next(0, 3) == 0 ? 0 : id;
        return Slot(slotId, _random.Next(minHp, maxHpExclusive));
    }

    private int[] RandomRolls(int n) => [.. Enumerable.Range(0, n).Select(_ => _random.Next(0, 0x8000))];

    private EnemyAiContext RandomContext(BattleTableAction[] actions, BattleTableAction counter)
    {
        int maxHp = _random.Next(0, 3000);
        var roster = new BattleFighter?[3];
        for (int k = 0; k < 3; k++)
        {
            roster[k] = _random.Next(4) == 0 ? null : RandomSlot(11 + k, -1, 3);
        }

        int playerMax = _random.Next(1, 2000);

        // Locals keep the order in which the random values were drawn by the inline argument list.
        var enemy = Enemy(stats: new EnemyStats(Hp: _random.Next(0, maxHp + 1), MaxHp: maxHp, Mp: _random.Next(0, 60), Charge: _random.Next(0, 3), Turns: _random.Next(0, 12)), boosts: [_random.Next(-1, 2), _random.Next(-1, 2), _random.Next(-1, 2), 0], techs: [_random.Next(1, 500), _random.Next(1, 500), _random.Next(1, 500)], actions: actions, counter: counter);
        BattleFighter? player = _random.Next(5) == 0 ? null : RandomPlayer(playerMax);
        return Ctx(
            enemy: enemy,
            player: player,
            enemies: roster,
            activeEnemy: _random.Next(0, 3),
            encounter: (_random.Next(0, 4), _random.Next(0, 4), _random.Next(0, 4)),
            starters: [1, 3, 5]);
    }

    private BattleFighter RandomPlayer(int maxHp)
    {
        int id = _random.Next(1, 8);
        int hp = _random.Next(0, maxHp + 1);
        int flags = _random.Next(2) == 0 ? FighterFlags.Asleep : 0;
        return Slot(id, hp, maxHp, flags);
    }

    private sealed record EnemyStats(int Hp = 50, int MaxHp = 100, int Mp = 10, int Charge = 0, int Turns = 0);
}
