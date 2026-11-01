using System.Text.Json.Nodes;
using Dw3Production.Simulation;

namespace Dw3.Game.Tests;

/// <summary>
/// Shared JSON encoding of the battle-enemy-ai differential vectors
/// (Vectors/battle-enemy-ai.json). The generator and the replay test both use it, so a
/// case is built from exactly the same fields in both directions. Every value is invented.
/// </summary>
internal static class BattleEnemyAiVectorCodec
{
    /// <summary>A scripted random source that also counts how many values were drawn.</summary>
    internal sealed class CountingRng : BattleRng
    {
        private readonly int[] _values;
        public int Calls { get; private set; }
        public CountingRng(int[] values) => _values = values;

        public int Next()
        {
            if (Calls >= _values.Length)
                throw new InvalidOperationException($"random sequence exhausted after {Calls} values");
            return _values[Calls++];
        }
    }

    public static int[] Ints(JsonNode? node) =>
        node is null ? [] : node.AsArray().Select(v => v!.GetValue<int>()).ToArray();

    public static JsonArray ToJson(IEnumerable<int> values) => new(values.Select(v => (JsonNode?)v).ToArray());

    public static JsonArray ToJson(BattleTableAction a) => ToJson([a.Target, a.Condition, a.ConditionArg]);

    public static BattleTableAction Action(JsonNode? node)
    {
        var v = Ints(node);
        return new BattleTableAction(v[0], v[1], v[2]);
    }

    /// <summary>The enemy fighter: only the fields the enemy AI reads.</summary>
    public static JsonObject EnemyToJson(BattleFighter f) => new()
    {
        ["hp"] = f.Hp,
        ["maxHp"] = f.MaxHp,
        ["mp"] = f.Mp,
        ["charge"] = f.Charge,
        ["turns"] = f.Turns,
        ["boosts"] = ToJson(f.Boosts),
        ["techs"] = ToJson(f.Techs),
        ["actions"] = new JsonArray(f.Actions.Select(a => (JsonNode?)ToJson(a)).ToArray()),
        ["counter"] = ToJson(f.Counter),
    };

    public static BattleFighter EnemyFromJson(JsonNode n) => new()
    {
        Hp = n["hp"]!.GetValue<int>(),
        MaxHp = n["maxHp"]!.GetValue<int>(),
        Mp = n["mp"]!.GetValue<int>(),
        Charge = n["charge"]!.GetValue<int>(),
        Turns = n["turns"]!.GetValue<int>(),
        Boosts = Ints(n["boosts"]),
        Techs = Ints(n["techs"]),
        Actions = n["actions"]!.AsArray().Select(Action).ToArray(),
        Counter = Action(n["counter"]),
    };

    /// <summary>A side fighter (active player or enemy roster slot): id, HP, max HP, flags.</summary>
    public static JsonNode? SlotToJson(BattleFighter? f) => f is null
        ? null
        : new JsonObject { ["id"] = f.Id, ["hp"] = f.Hp, ["maxHp"] = f.MaxHp, ["flags"] = f.Flags };

    public static BattleFighter? SlotFromJson(JsonNode? n) => n is null
        ? null
        : new BattleFighter
        {
            Id = n["id"]!.GetValue<int>(),
            Hp = n["hp"]!.GetValue<int>(),
            MaxHp = n["maxHp"]!.GetValue<int>(),
            Flags = n["flags"]!.GetValue<int>(),
        };

    public static JsonObject ContextToJson(EnemyAiContext c) => new()
    {
        ["enemy"] = EnemyToJson(c.Enemy),
        ["activePlayer"] = SlotToJson(c.ActivePlayer),
        ["enemies"] = new JsonArray(c.Enemies.Select(SlotToJson).ToArray()),
        ["activeEnemy"] = c.ActiveEnemy,
        ["boostElement"] = c.BoostElement,
        ["battleId"] = c.BattleId,
        ["encounterKind"] = c.EncounterKind,
        ["starters"] = ToJson(c.StarterDigimonIds),
    };

    public static EnemyAiContext ContextFromJson(JsonNode n) => new()
    {
        Enemy = EnemyFromJson(n["enemy"]!),
        ActivePlayer = SlotFromJson(n["activePlayer"]),
        Enemies = n["enemies"]!.AsArray().Select(SlotFromJson).ToArray(),
        ActiveEnemy = n["activeEnemy"]!.GetValue<int>(),
        BoostElement = n["boostElement"]!.GetValue<int>(),
        BattleId = n["battleId"]!.GetValue<int>(),
        EncounterKind = n["encounterKind"]!.GetValue<int>(),
        StarterDigimonIds = Ints(n["starters"]),
    };

    public static JsonObject DecisionToJson(EnemyDecision d) => new()
    {
        ["kind"] = d.Kind.ToString(),
        ["techId"] = d.TechId,
        ["switchSlot"] = d.SwitchSlot,
    };

    public static EnemyDecision DecisionFromJson(JsonNode n) => new(
        Enum.Parse<EnemyActionKind>(n["kind"]!.GetValue<string>()),
        n["techId"]!.GetValue<int>(),
        n["switchSlot"]!.GetValue<int>());

    /// <summary>
    /// Runs one case against the current implementation and returns its recorded output:
    /// the result, the number of random values drawn, or the exception type thrown.
    /// </summary>
    public static JsonObject Evaluate(JsonNode c)
    {
        string fn = c["fn"]!.GetValue<string>();
        var rng = new CountingRng(Ints(c["rng"]));
        var output = new JsonObject();
        try
        {
            switch (fn)
            {
                case "TestEnemyCondition":
                    output["result"] = BattleEnemyAi.TestEnemyCondition(
                        c["condition"]!.GetValue<int>(), c["arg"]!.GetValue<int>(),
                        ContextFromJson(c["ctx"]!), rng);
                    break;
                case "GetEnemyAction":
                    output["decision"] = DecisionToJson(BattleEnemyAi.GetEnemyAction(
                        EnemyFromJson(c["enemy"]!), c["targetKind"]!.GetValue<int>()));
                    break;
                case "PickAction":
                {
                    var ctx = ContextFromJson(c["ctx"]!);
                    output["decision"] = DecisionToJson(BattleEnemyAi.PickAction(ctx.Enemy, ctx, rng));
                    break;
                }
                case "PickCounter":
                {
                    var ctx = ContextFromJson(c["ctx"]!);
                    output["decision"] = DecisionToJson(BattleEnemyAi.PickCounter(ctx.Enemy, ctx, rng));
                    break;
                }
                case "ResolveSwitch":
                    output["slot"] = BattleEnemyAi.ResolveSwitch(
                        DecisionFromJson(c["decision"]!),
                        c["enemies"]!.AsArray().Select(SlotFromJson).ToArray(),
                        c["activeEnemy"]!.GetValue<int>(), rng);
                    break;
                case "DecisionFlags":
                {
                    var d = DecisionFromJson(c["decision"]!);
                    output["flags"] = new JsonObject
                    {
                        ["isAttack"] = d.IsAttack,
                        ["isTechnique"] = d.IsTechnique,
                        ["isSwitch"] = d.IsSwitch,
                        ["isRunAway"] = d.IsRunAway,
                    };
                    break;
                }
                default:
                    throw new InvalidDataException($"unknown vector function '{fn}'");
            }
        }
        catch (Exception e) when (e is IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            output = new JsonObject { ["throws"] = e.GetType().Name };
        }
        output["rngCalls"] = rng.Calls;
        return output;
    }
}
