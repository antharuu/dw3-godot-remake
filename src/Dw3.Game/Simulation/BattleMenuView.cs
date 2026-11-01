using Dw3Production.Conversion;

namespace Dw3Production.Simulation;

/// <summary>The engine-free view of the battle menu, with its source page slice.</summary>
public sealed record BattleMenuView(BattleMenuKind Menu, int Cursor, int First, int PageSize, int PageCount, IReadOnlyList<BattleMenuLine> Lines,
    BattleTextLine? PreviousPageText, BattleTextLine? NextPageText, BattleMenuStatusText? StatusText);
