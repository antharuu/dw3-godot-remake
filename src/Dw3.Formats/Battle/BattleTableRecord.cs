namespace Dw3.Formats.Battle;

/// <summary>One enemy battle configuration entry from SDIGIEDT.PRO.</summary>
public sealed record BattleTableRecord(
    short Id,
    short Item,
    short ItemChance,
    short NameId,
    IReadOnlyList<short> Techniques,
    IReadOnlyList<short> Stats,
    IReadOnlyList<short> Resistances,
    byte Family,
    IReadOnlyList<BattleAction> Actions,
    BattleAction Counter);
