using Dw3Production.Conversion;

namespace Dw3Production.Presentation;

/// <summary>One side's fighter, as the HUD draws it.</summary>
public readonly record struct BattleFighterView(
    int Id,
    BattleTextLine? Name,
    int Hp,
    int MaxHp,
    int Mp,
    int MaxMp,
    bool Active,
    bool KnockedOut,
    int BlastGauge = 0);
