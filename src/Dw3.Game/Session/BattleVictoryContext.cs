using Dw3Production.Conversion;
using Dw3Production.Simulation;

namespace Dw3Production.Session;

/// <summary>Groups the encounter-specific inputs of a victory reward that the session does not own.</summary>
/// <param name="Reward">Experience, Digimon experience and money of the encounter.</param>
/// <param name="Encounter">Encounter id used by the structured reward diagnostic.</param>
/// <param name="Fighters">Decoded fighter table supplying each partner's growth curve.</param>
/// <param name="GrowthTables">Decoded growth tables, or null when growth cannot be applied.</param>
/// <param name="Random">Shared battle stream used by the drop and level-up rolls.</param>
/// <param name="NameFor">Decoded display text for a fighter id.</param>
/// <param name="Text">Battle text catalogue attached to the report.</param>
/// <param name="Prize">Scripted prize item forced by the encounter, or null when the enemy drop decides.</param>
/// <param name="Digivolutions">Decoded evolution requirements used for post-victory form progression.</param>
public sealed record BattleVictoryContext(
    BattleReward Reward,
    int Encounter,
    FighterData Fighters,
    GrowthTables? GrowthTables,
    IBattleRng Random,
    Func<int, BattleTextLine> NameFor,
    BattleTextCatalog Text,
    int? Prize = null,
    DigivolutionData? Digivolutions = null);
