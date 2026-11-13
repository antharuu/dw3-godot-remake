using Dw3.Data;
using Dw3Production.Conversion;
using Dw3Production.Diagnostics.Field;
using Dw3Production.Simulation;
using Godot;
using TechData = Dw3Production.Simulation.TechData;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Qualifies item 0x5A against a caller-supplied prepared pack through the native item menu.</summary>
internal static class NativePreparedItemTechniqueProof
{
    /// <summary>Runs only when the explicit external-pack diagnostic flag is enabled.</summary>
    public static void RunWhenRequested()
    {
        if (System.Environment.GetEnvironmentVariable("DW3_PREPARED_ITEM_PROOF") != "1")
        {
            return;
        }

        string root = System.Environment.GetEnvironmentVariable("DW3_DATA_DIR") ?? throw new InvalidOperationException("Prepared item proof requires DW3_DATA_DIR.");
        using var loader = new BattleDataPackLoader(new DataPackReader(root));
        var data = new NativeListData(loader);
        TechData prepared = data.ItemTechnique;
        using var session = new NativeMenuSession(data.BuildItemTechniqueFlow(), data.Text, data.ItemTechniqueItems);
        session.Flow.State.AllowCritical = true;
        int hp = session.Enemy.Hp;
        int mp = session.Player.Mp;
        int expected = ExpectedCriticalDamage(prepared, session.Player, session.Enemy);

        NativeMenuCommandsProof.Select(session, 4);
        session.Tap(Key.Enter);
        ProbeRequirement.Require(session.Menu.Menu == BattleMenuKind.Item && session.Menu.Lines.Count == 1, "prepared item menu did not open through GameHost input");
        session.Tap(Key.Enter);

        ProbeRequirement.Require(session.Enemy.Hp == hp - expected, $"prepared item technique damage oracle mismatch expected={expected} actual={hp - session.Enemy.Hp}");
        ProbeRequirement.Require(session.Player.Mp == mp, "prepared item technique paid MP");
        ProbeRequirement.Require(session.Flow.ItemStock[0x5A] == 1, "prepared item technique stock mismatch");
        ProbeRequirement.Require(session.Flow.Phase == BattleFlowPhase.EnemyTurn, "prepared item technique did not spend its turn");
        ProbeRequirement.Require(session.Rng.Count == 2, "prepared item technique critical draw count mismatch");
        GD.Print($"NATIVE_PREPARED_ITEM_PROOF PASS tech={prepared.Power}/{prepared.Icon}/{prepared.Kind}/{prepared.Accuracy}/{prepared.Element}/{prepared.Family} damage={expected} stock=1 mp={mp} rng={session.Rng.Count}");
    }

    /// <summary>Independently calculates this proof's forced-minimum-critical magic result from the loaded record and resolved fighters.</summary>
    /// <param name="tech">Prepared item technique.</param>
    /// <param name="player">Resolved active partner.</param>
    /// <param name="enemy">Resolved active enemy.</param>
    /// <returns>Expected direct-magic damage with critical strength zero.</returns>
    private static int ExpectedCriticalDamage(TechData tech, BattleFighter player, BattleFighter enemy)
    {
        player.RefreshStats();
        enemy.RefreshStats();
        int spirit = player.Stats.Stats[BattleStat.Spirit] * 50 / enemy.Stats.Stats[BattleStat.Spirit];
        int power = Math.Clamp(tech.Power * (spirit + 50) / 100, tech.Power / 2, tech.Power * 2);
        int resistance = enemy.Stats.Resist[tech.Element - BattleConst.ElementFirst];
        int damage = MagicAfterResistance(power, resistance);
        if (tech.Family >= BattleConst.FamilyFirst && tech.Family == enemy.Stats.Family)
        {
            damage += damage / 2;
        }

        damage += damage / 2;
        damage = Math.Max(damage - enemy.Stats.DamageCut, 0);
        return Math.Min(damage, power * 5);
    }

    /// <summary>Applies the loaded target's piecewise magic resistance without calling the production calculator.</summary>
    /// <param name="power">Magic damage before resistance.</param>
    /// <param name="resistance">Loaded elemental resistance.</param>
    /// <returns>Resistance-adjusted magic damage.</returns>
    private static int MagicAfterResistance(int power, int resistance)
    {
        if (resistance < 100)
        {
            return power * (400 - (resistance * 3)) / 100;
        }

        if (resistance >= 300)
        {
            return power * (65 - (resistance / 20)) / 100;
        }

        return power * (125 - (resistance / 4)) / 100;
    }
}
