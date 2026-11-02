namespace Dw3Production.Diagnostics.Battle;

/// <summary>Renders every decoded fighter into an offline diagnostic sheet.</summary>
internal sealed class FighterSheetProbe : BattleProbe
{
    /// <summary>Initializes a new instance of the <see cref="FighterSheetProbe"/> class.</summary>
    /// <param name="host">Restricted live battle host.</param>
    public FighterSheetProbe(IBattleProbeHost host)
        : base(host)
    {
    }

    /// <inheritdoc/>
    protected override string FailureCode => "FIGHTER_SHEET_PROBE";

    /// <inheritdoc/>
    protected override Task ExecuteAsync() { Host.RunFighterSheet(); return Task.CompletedTask; }
}
