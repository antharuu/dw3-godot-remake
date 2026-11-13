namespace Dw3Production.Simulation;

/// <summary>
/// Commits a won battle's deferred awards at the report stages where the original credits them: each partner's
/// experience, growth and forms when its raise starts, the money when its page opens and the item when its page opens.
/// Every stage commits at most once.
/// </summary>
public interface IFightReportSettlement
{
    /// <summary>Gets the award snapshot taken at the battle's end, before anything was credited.</summary>
    FightReportInput Snapshot { get; }

    /// <summary>Credits one partner's experience and form progression, once, and returns its settled report row.</summary>
    /// <param name="snapshot">The partner's row from <see cref="Snapshot"/>.</param>
    /// <returns>The row with the level reached, the used-form rows and the forms acquired.</returns>
    FightReportPartner RaisePartner(FightReportPartner snapshot);

    /// <summary>Credits the money award, once.</summary>
    void CreditMoney();

    /// <summary>Credits the item award, once. Does nothing when the battle dropped no item.</summary>
    void CreditItem();
}
