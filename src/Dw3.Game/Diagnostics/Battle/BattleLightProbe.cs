namespace Dw3Production.Diagnostics.Battle;

/// <summary>Verifies the stage-18 fighter lighting and private reference comparison.</summary>
internal sealed class BattleLightProbe : BattleProbe
{
    /// <summary>Initializes a new instance of the <see cref="BattleLightProbe"/> class.</summary>
    /// <param name="host">Restricted live battle host.</param>
    public BattleLightProbe(IBattleProbeHost host)
        : base(host)
    {
    }

    /// <inheritdoc/>
    protected override string FailureCode => "BATTLE_LIGHT_PROBE";

    /// <inheritdoc/>
    protected override Task ExecuteAsync() { Host.RunBattleLightProbe(); return Task.CompletedTask; }
}
