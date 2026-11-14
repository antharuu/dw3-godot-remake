using System.Globalization;
using Dw3Production.Diagnostics.Field;
using Dw3Production.Logging;
using Dw3Production.Presentation;
using Dw3Production.Simulation;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>
/// Proves that a blast-return form change on the active slot starts the source entrance on the production frame
/// coordinator, renderer and physical steps instead of an instant rebuild, and that the caller's message follows the
/// entrance per <see cref="BattleEntranceKind.PlayerBlastReturn"/> (MFIGHT 0x62, B9-058d).
/// The fighter's blast form is taken from the production blast catalogue built from the pack; the state is forced into
/// the temporary form before the probe starts so the proof is deterministic and does not require a live gauge charge.
/// </summary>
internal sealed class BattleBlastReturnProbe : BattleProbe
{
    /// <summary>Post-message units before the scripted Cross.</summary>
    private const int ConfirmAfter = 75;

    /// <summary>Initializes a new instance of the <see cref="BattleBlastReturnProbe"/> class.</summary>
    /// <param name="host">Restricted live battle host.</param>
    public BattleBlastReturnProbe(IBattleProbeHost host)
        : base(host)
    {
    }

    /// <inheritdoc/>
    protected override string FailureCode => "BATTLE_BLAST_RETURN_PROBE";

    /// <inheritdoc/>
    protected override async Task ExecuteAsync()
    {
        string root = Host.PrepareCaptureDirectory("probe/battle-blast-return");
        Host.ApplyS200();
        var driver = new BattleProductionFrameDriver(Host.FrameHost);

        BattleFighter player = Host.Flow.State.ActivePlayerFighter
            ?? throw new InvalidOperationException("blast-return probe: no active player fighter after ApplyS200");

        int originalId = player.Id;
        int blastFormId = player.Blast?.FormId
            ?? throw new InvalidOperationException($"blast-return probe: active fighter {originalId} has no blast configuration; B9-058d requires a blast-capable fighter in the debug session");

        ProbeRequirement.Require(blastFormId != originalId && blastFormId != 0, $"blast-return probe: blast form id {blastFormId} must differ from base id {originalId}");
        int idle = BattleWeakIdleRule.Select(player.Hp, 0, player.MaxHp);

        // Force the fighter into the blast state without going through the gauge trigger, so the proof is
        // deterministic (no RNG, no enemy turns needed to charge the gauge). The entrance sequencer only cares
        // about the form change and the cause exposed by the state; it does not require the blast to have been
        // charged normally.
        player.PrevId = originalId;
        player.Id = blastFormId;
        player.Temporary = true;
        Host.Flow.State.NotifyActiveFormChanged(BattleScheduledEventKind.BlastReturn);

        // Advance one production tick: RefreshModels reads the pending BlastReturn cause, starts the entrance call.
        driver.Advance(1.0 / 60.0);
        await Host.NextFrameDrawn();

        ProbeRequirement.Require(Host.ActorEntranceCall is not null, "blast-return probe: entrance call did not start after one tick");
        ProbeRequirement.Require(!Host.ActorEntranceCall!.Sequence.Enemy, "blast-return probe: entrance started on the enemy side");

        var proof = new BattleEntranceCallProof(Host, driver, root);
        var expected = new BattleEntranceExpectation("blast-return", BattleEntranceKind.PlayerBlastReturn, blastFormId, originalId, idle, ConfirmAfter);
        await proof.RunAsync(expected);

        QuietLog.Info(string.Create(CultureInfo.InvariantCulture, $"battle-blast-return probe PASS original={originalId} blast={blastFormId} idle={idle} ticks={driver.Ticks} captures={root}"));
    }
}
