using Dw3Production.Conversion;
using Dw3Production.Simulation;

namespace Dw3Production;

/// <summary>Builds source-qualified pair confirmation rows without owning mutable host state.</summary>
public static class BattlePairMenuEntries
{
    /// <summary>Builds the ordinary switch row and, when decoded data defines it, the pair-technique row.</summary>
    /// <param name="flow">Active battle flow.</param>
    /// <param name="text">Decoded battle text catalogue.</param>
    /// <returns>Semantic action arguments and decoded labels.</returns>
    public static IReadOnlyList<(int Action, BattleTextLine Text)> Build(BattleFlowController flow, BattleTextCatalog text)
    {
        ArgumentNullException.ThrowIfNull(flow);
        ArgumentNullException.ThrowIfNull(text);
        var result = new List<(int Action, BattleTextLine Text)> { (0, text.Command(3)) };
        BattleFighter? active = flow.State.ActivePlayerFighter;
        if (active is not null && flow.PairTechniquesByForm.TryGetValue(active.Id, out PairTechniqueDefinition? definition))
        {
            result.Add((1, flow.Text?.Technique(definition.TechniqueId) ?? BattleTextLine.Missing));
        }

        return result;
    }
}
