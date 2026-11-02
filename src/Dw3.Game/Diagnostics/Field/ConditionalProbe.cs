using Dw3Production.Logging;
using Dw3Production.Simulation;
using static Dw3Production.Diagnostics.Field.ProbeRequirement;

namespace Dw3Production.Diagnostics.Field;

/// <summary>
/// M3 story-gated exits S315 slot 0 -> S320 and S320 slot 1 -> S315. During exploration a gated
/// exit stays crossable: its raw condition is kept unevaluated, standing on its trigger must offer
/// it and log <c>exit-condition-not-enforced</c>, never suppress it. The player is placed directly
/// on the trigger, like the warp probe, since this is not the interactive session.
/// </summary>
internal sealed class ConditionalProbe : FieldProbe
{
    /// <summary>Initializes a new instance of the <see cref="ConditionalProbe"/> class.</summary>
    /// <param name="host">Running game.</param>
    public ConditionalProbe(IFieldProbeHost host)
        : base(host)
    {
    }

    /// <inheritdoc/>
    protected override string FailureCode => "CONDITIONAL_PROBE";

    /// <inheritdoc/>
    protected override Task ExecuteAsync()
    {
        AssertGatedExitCrossableAndSignaled("S315", 0);
        AssertGatedExitCrossableAndSignaled("S320", 1);
        QuietLog.Info("conditional probe PASS S315.slot0->S320 S320.slot1->S315 crossable-signaled");
        return Task.CompletedTask;
    }

    /// <summary>Stands on a gated exit and checks the offer and the warning.</summary>
    /// <param name="zoneId">Zone of the exit.</param>
    /// <param name="slot">Exit slot.</param>
    private void AssertGatedExitCrossableAndSignaled(string zoneId, int slot)
    {
        var zone = Host.GetOrLoadZone(zoneId);
        var exit = zone.Zone.Exits.FirstOrDefault(item => item.Slot == slot);
        Require(exit is not null && exit.IsUnevaluated, $"{zoneId} slot{slot} gated exit is not exposed as unevaluated");
        Host.State.ZoneId = zoneId;
        Host.ApplyScene(zone);
        Host.State.Position = TriggerSlotLocator.FindCell(zone, slot);
        Host.State.SetFacing(TriggerSlotLocator.FindDirection(zone, slot));
        Host.Input.State.ReleaseAction();
        var warnings = new List<string>();
        Host.Warp.Tick(Host.State, Host.Input.State, zone, Host.CreateWarpCallbacks((key, message) => warnings.Add($"{key}:{message}")));
        Require(Host.Warp.State == WarpState.Offer && Host.State.Offer, $"gated {zoneId} slot{slot} must offer");
        Require(
            warnings.Any(warning => warning.StartsWith("exit-condition-not-enforced:", StringComparison.Ordinal)),
            $"{zoneId} slot{slot} did not log exit-condition-not-enforced (got: {string.Join("; ", warnings)})");
    }
}
