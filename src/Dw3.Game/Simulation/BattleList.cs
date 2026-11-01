namespace Dw3Production.Simulation;

/// <summary>
/// The eight battles of one field area, picked from at random (field_map.h BattleList).
/// Count indexes FIELDSTG_battleRates: how much each walk step lowers the countdown.
/// </summary>
public sealed class BattleList
{
    /// <summary>Initializes a new instance of the <see cref="BattleList"/> class.</summary>
    /// <param name="count">BattleList.Count: the rate index into FIELDSTG_battleRates.</param>
    /// <param name="battles">The eight area battles; exactly eight entries are required.</param>
    /// <exception cref="ArgumentException">Thrown when <paramref name="battles"/> does not have exactly eight entries.</exception>
    public BattleList(int count, IReadOnlyList<AreaBattle> battles)
    {
        if (battles.Count != 8)
        {
            throw new ArgumentException("a BattleList holds exactly eight battles", nameof(battles));
        }

        Count = count;
        Battles = battles;
    }

    /// <summary>Gets the rate index into FIELDSTG_battleRates.</summary>
    public int Count { get; }

    /// <summary>Gets the eight area battles.</summary>
    public IReadOnlyList<AreaBattle> Battles { get; }
}
