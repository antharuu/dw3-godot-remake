using Dw3Production.Conversion;
using Dw3Production.Simulation;

namespace Dw3Production;

/// <summary>Contains a pack-free native menu fixture constructor, with production callbacks unchanged.</summary>
public partial class GameHost
{
    /// <summary>Initializes a new instance of the <see cref="GameHost"/> class for isolated native menu proofs.
    /// The normal constructor creates the actual coordinator. Only session data is supplied here.
    /// The node is deliberately not added to a tree, so field startup and visible input never run.</summary>
    /// <param name="flow">Synthetic initialized battle flow.</param>
    /// <param name="text">Synthetic text tables.</param>
    /// <param name="items">Synthetic item metadata.</param>
    /// <param name="random">Counting simulation random source.</param>
    internal GameHost(BattleFlowController flow, BattleTextData text, BattleItemsData items, IBattleRng random)
        : this()
    {
        _battleFlow = flow;
        _battleCatalogForPartner = flow.State.ActivePlayer;
        _battleTextData = text;
        _battleItemsData = items;
        _battleRng = random;
    }
}
