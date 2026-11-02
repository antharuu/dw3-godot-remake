namespace Dw3Production.Diagnostics.Battle;

/// <summary>Verifies decoded fighter model rendering, skeletal masks, and surface passes.</summary>
internal sealed class BattleModelProbe : BattleProbe
{
    /// <summary>Initializes a new instance of the <see cref="BattleModelProbe"/> class.</summary>
    /// <param name="host">Restricted live battle host.</param>
    public BattleModelProbe(IBattleProbeHost host)
        : base(host)
    {
    }

    /// <inheritdoc/>
    protected override string FailureCode => "BATTLE_MODEL_PROBE";

    /// <inheritdoc/>
    protected override Task ExecuteAsync() { Host.RunBattleModelProbe(); return Task.CompletedTask; }
}
