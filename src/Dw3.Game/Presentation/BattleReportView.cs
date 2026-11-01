using Dw3Production.Conversion;
using Dw3Production.Simulation;

namespace Dw3Production.Presentation;

/// <summary>The victory report: the battle's real reward (exp share, money, digimonExp) and
/// each fought partner's exp. Null while the battle is not resolved.</summary>
public readonly record struct BattleReportView(BattleOutcome Outcome, int Exp, int Money, int DigimonExp, IReadOnlyList<BattlePartnerReportView> Partners, BattleTextCatalog Text);
