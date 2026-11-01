using System.Text.Json.Nodes;
using Xunit;

namespace Dw3.Game.Tests;

/// <summary>
/// Differential regression suite for the enemy battle AI. Replays every fabricated case of
/// Vectors/battle-enemy-ai.json against the current implementation and compares the result,
/// the number of random values drawn, or the exception type thrown.
/// </summary>
public sealed class BattleEnemyAiVectorTests
{
    private static readonly string VectorsPath =
        Path.Combine(AppContext.BaseDirectory, "Vectors", "battle-enemy-ai.json");

    private static JsonArray LoadCases()
    {
        Assert.True(File.Exists(VectorsPath), $"Vectors file not found: {VectorsPath}");
        return JsonNode.Parse(File.ReadAllText(VectorsPath))!["cases"]!.AsArray();
    }

    [Fact]
    public void VectorCount_AtLeast300() => Assert.True(LoadCases().Count >= 300);

    [Fact]
    public void EveryPublicEntryPointIsCovered()
    {
        var functions = LoadCases().Select(c => c!["fn"]!.GetValue<string>()).ToHashSet();
        foreach (var fn in new[] { "TestEnemyCondition", "GetEnemyAction", "PickAction", "PickCounter", "ResolveSwitch", "DecisionFlags" })
            Assert.Contains(fn, functions);
    }

    [Fact]
    public void AllVectorsMatchCurrentImplementation()
    {
        var errors = new List<string>();
        foreach (var c in LoadCases())
        {
            var actual = BattleEnemyAiVectorCodec.Evaluate(c!);
            var expected = c!["expected"]!;
            if (!JsonNode.DeepEquals(actual, expected))
                errors.Add($"case {c["id"]} ({c["note"]}): expected {expected.ToJsonString()}, got {actual.ToJsonString()}");
        }
        Assert.True(errors.Count == 0, string.Join("\n", errors.Take(20)));
    }
}
