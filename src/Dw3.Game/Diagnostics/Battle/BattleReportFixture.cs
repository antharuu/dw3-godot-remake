using System.Globalization;
using Dw3Production.Session;
using Dw3Production.Simulation;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>
/// A disclosed report-probe input, selected by <c>DW3_REPORT_FIXTURE</c>, that the native reference run reproduces with
/// the same values: the encounter, the lead partner's level, the wallet, a scripted prize with its stock and the
/// opponent's HP. It only prepares state through existing game mechanisms, never a reward, page or outcome.
/// </summary>
/// <param name="Name">Fixture name.</param>
/// <param name="Encounter">Encounter fought, 200 through the production F8 route.</param>
/// <param name="LeadLevel">Lead partner level before the battle, its experience reset to 0, or null to keep the session's.</param>
/// <param name="Wallet">Wallet before the battle, or null to keep the session's.</param>
/// <param name="PrizeItem">Scripted prize item id, 0 for none.</param>
/// <param name="PrizeStock">Stock of the prize item before the battle.</param>
/// <param name="EnemyHp">Opponent HP after the battle starts, or null to keep the encounter's.</param>
internal sealed record BattleReportFixture(string Name, int Encounter, int? LeadLevel, int? Wallet, int PrizeItem, int PrizeStock, int? EnemyHp)
{
    /// <summary>Environment variable selecting the fixture.</summary>
    public const string Variable = "DW3_REPORT_FIXTURE";

    /// <summary>Gets the default session on the production F8 encounter: no level-up, money, no item.</summary>
    public static BattleReportFixture Normal { get; } = new("normal", 200, null, null, 0, 0, null);

    /// <summary>Gets several lead level-ups, a scripted prize into a full stock and a wallet that reaches its cap.</summary>
    public static BattleReportFixture LevelsLootCap { get; } = new("levels-loot-cap", 48, 1, PlayerSession.MoneyMax - 10, 43, 99, 1);

    /// <summary>Reads the fixture named by <see cref="Variable"/>.</summary>
    /// <returns>The selected fixture, <see cref="Normal"/> by default.</returns>
    /// <exception cref="InvalidOperationException">The name is unknown.</exception>
    public static BattleReportFixture FromEnvironment() => System.Environment.GetEnvironmentVariable(Variable) switch
    {
        null or "" or "normal" => Normal,
        "levels-loot-cap" => LevelsLootCap,
        var other => throw new InvalidOperationException($"unknown {Variable}={other}"),
    };

    /// <summary>Prepares the session before the battle.</summary>
    /// <param name="host">Live probe host.</param>
    /// <exception cref="InvalidOperationException">The session or its lead growth is unavailable.</exception>
    public void PrepareSession(IBattleProbeHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        PlayerSession session = host.Session ?? throw new InvalidOperationException("no session to prepare");
        if (LeadLevel is int level)
        {
            PartnerGrowthState growth = session.Partners[0]?.Growth ?? throw new InvalidOperationException("the lead partner has no growth");
            growth.Level = level;
            growth.Exp = 0;
        }

        if (Wallet is int wallet && wallet > session.Money)
        {
            session.CreditMoney(wallet - session.Money);
        }

        if (PrizeItem > 0)
        {
            session.Inventory[PrizeItem] = PrizeStock;
        }
    }

    /// <summary>Starts the fixture's battle and applies its battle-side state.</summary>
    /// <param name="host">Live probe host.</param>
    public void StartBattle(IBattleProbeHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (Encounter == 200)
        {
            host.StartDebugBattle();
        }
        else
        {
            host.StartEncounterForProbe(Encounter);
        }

        if (PrizeItem > 0)
        {
            BattleSetup battle = host.ActiveBattle ?? throw new InvalidOperationException("no active battle receives the prize");
            battle.HasPrize = true;
            battle.Prize = PrizeItem;
        }

        if (EnemyHp is int hp && host.Flow.State.ActiveEnemyFighter is { } enemy)
        {
            enemy.Hp = hp;
        }
    }

    /// <summary>Describes every partner's persistent state, the values the native fixture writes.</summary>
    /// <param name="host">Live probe host.</param>
    /// <returns>One line per party slot.</returns>
    public static IReadOnlyList<string> DescribeParty(IBattleProbeHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        PlayerSessionPartner?[] partners = host.Session?.Partners ?? [];
        var lines = new List<string>();
        for (int index = 0; index < partners.Length; index++)
        {
            if (partners[index] is { } partner && partner.Growth is { } growth)
            {
                lines.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"slot={index} base={partner.OriginFormId} level={growth.Level} exp={growth.Exp} tp={growth.Tp} maxHp={growth.MaxHp} maxMp={growth.MaxMp} stats=[{string.Join(",", growth.BattleStats)}] resists=[{string.Join(",", growth.Resistances)}] known=[{string.Join(",", partner.KnownForms)}] equipped=[{string.Join(",", partner.EquippedForms)}] equipment=[{string.Join(",", partner.EquipmentIds)}]"));
            }
        }

        return lines;
    }
}
