using Dw3.Data.Battle;

namespace Dw3Production.Conversion;

/// <summary>Loads a decoded action-script for a given fighter and technique script-selector.
/// Returns null when the script is not in the first-batch lot or is otherwise unavailable,
/// allowing the caller to skip the sequence gracefully.</summary>
public interface IActionScriptProvider
{
    /// <summary>Tries to load the action script for the given fighter and selector.</summary>
    /// <param name="fighterId">Fighter id.</param>
    /// <param name="scriptSelector">Technique script selector from <c>TechData.Script</c>.</param>
    /// <returns>The decoded script, or <see langword="null"/> when it is not available.</returns>
    BattleActionScript? TryLoad(int fighterId, int scriptSelector);
}
