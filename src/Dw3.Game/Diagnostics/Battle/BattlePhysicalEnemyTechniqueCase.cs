using Dw3Production.Conversion;
using Dw3Production.Diagnostics.Field;
using Dw3Production.Simulation;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Prepares a natural enemy-owned multi-hit contact technique without replacing its selector.</summary>
internal static class BattlePhysicalEnemyTechniqueCase
{
    /// <summary>Exercises the enemy's actual second technique after the real player's opening attack.</summary>
    /// <param name="host">Offscreen production host.</param>
    /// <param name="loader">Validated pack.</param>
    /// <param name="root">Private captures.</param>
    /// <param name="mode">Outcome branch.</param>
    /// <param name="play">Shared physical runner and audit.</param>
    /// <param name="selected">Natural fighter and technique, or the retained first-lot enemy case.</param>
    /// <returns>Completion after actual enemy return.</returns>
    public static async Task RunAsync(IBattleProbeHost host, BattleDataPackLoader loader, string root, string mode, Func<BattleAttackProofRng, string, Task> play, (int Fighter, int Technique) selected = default)
    {
        var identity = selected == default ? (Fighter: 178, Technique: 398) : selected;
        host.StartDebugBattle();
        await host.NextFrameDrawn();
        var flow = host.Flow;
        var enemy = flow.State.ActiveEnemyFighter ?? throw new InvalidOperationException("No enemy.");
        var player = flow.State.ActivePlayerFighter ?? throw new InvalidOperationException("No player.");
        enemy.Id = identity.Fighter;
        enemy.Hp = enemy.MaxHp = 9000;
        enemy.Counter = default;
        enemy.Techs = [.. loader.LoadFighters().BattleTable.Single(entry => entry.Id == enemy.Id).Techs];
        int slot = Array.IndexOf(enemy.Techs, identity.Technique);
        ProbeRequirement.Require(slot >= 0, "technique has no natural enemy slot");
        enemy.Actions = [new BattleTableAction(slot + 1, 0, 0)];
        host.Renderer.BuildModels(loader.ModelPath(player.Id), loader.ModelPath(enemy.Id), "player", "physical proof", enemy.Id);
        host.Renderer.ConfigureActionAssets(loader);
        host.Renderer.SequencePlayback.AssetConsumer = request => request is SoundRequestEvent ? SequenceAcknowledgement.Applied : SequenceAcknowledgement.Unavailable;
        string captures = Path.Combine(root, $"{enemy.Id}-{mode}");
        Directory.CreateDirectory(captures);
        await BattleProbeCapture.CaptureAsync(host, captures, "before.png");
        var openingRng = new BattleAttackProofRng(0);
        ProbeRequirement.Require(flow.Choose(BattleCommand.Attack, 0, openingRng), "opening attack refused");
        await play(openingRng, captures + "/opening");
        ProbeRequirement.Require(flow.Phase == BattleFlowPhase.EnemyTurn, "enemy turn unavailable");
        player.Hp = player.MaxHp = mode.StartsWith("fatal", StringComparison.Ordinal) ? 1 : 9000;
        var tech = BattleSetupFactory.ToTech(loader.LoadTechs().Techs[identity.Technique]);
        tech.Accuracy = mode == "miss" ? 1 : tech.Accuracy;
        if (slot == 0)
        {
            flow.EnemyBasicTech = tech;
        }

        flow.Techs = new Dictionary<int, Dw3Production.Simulation.TechData> { [identity.Technique] = tech };
        flow.State.Techs = flow.Techs;
        if (mode == "fatal-final")
        {
            foreach (var reserve in flow.State.Players.Skip(1).OfType<BattleFighter>())
            {
                reserve.Hp = 0;
            }
        }

        var rng = new BattleAttackProofRng(mode == "miss" ? 127 : 0);
        flow.EnemyAct(rng);
        ProbeRequirement.Require(flow.PendingSequenceContext is { } context && context.AttackerId == enemy.Id && context.ScriptSelector == tech.Script, "wrong enemy technique");
        ProbeRequirement.Require(flow.State.LastHitEvents.All(hit => hit.Landed == (mode != "miss")), "wrong enemy branch");
        await play(rng, captures);
        ProbeRequirement.Require(player.IsKnockedOut == mode.StartsWith("fatal", StringComparison.Ordinal), "wrong enemy KO");
    }
}
