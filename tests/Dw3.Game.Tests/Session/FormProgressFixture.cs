using Dw3Production;
using Dw3Production.Conversion;
using Dw3Production.Session;
using Dw3Production.Simulation;

namespace Dw3.Game.Tests.Session;

/// <summary>
/// Fabricated party, forms and evolution data for form-progression settlement tests. Every value is invented
/// for the test ledger, nothing is read from the game.
/// </summary>
internal static class FormProgressFixture
{
    /// <summary>Technique of skill entry zero, acquired at form level 2 and mastered at form level 5 by default.</summary>
    public const int AcquiredTechnique = 11;

    /// <summary>Technique of skill entry one, acquired at form level 3 by default.</summary>
    public const int LateTechnique = 12;

    /// <summary>Form unlocked by the fabricated evolution tree.</summary>
    public const int UnlockedForm = 8;

    /// <summary>Gets the three equipped forms every fabricated partner owns, in slot order.</summary>
    public static IReadOnlyList<int> EquippedForms { get; } = [5, 6, 7];

    /// <summary>Builds a three-partner session whose partners own and equip forms 5, 6 and 7.</summary>
    /// <param name="level">Partner level before the battle's own experience.</param>
    /// <param name="known">Acquired forms besides the base, defaulting to the three equipped forms.</param>
    /// <param name="equipped">Equipped forms of the first partner, defaulting to forms 5, 6 and 7.</param>
    /// <returns>A session whose partners share the same equipped form ids.</returns>
    public static PlayerSession Session(int level, IReadOnlyList<int>? known = null, IReadOnlyList<int>? equipped = null) =>
        new([Partner(1, level, known, equipped), Partner(2, level, null, null), Partner(3, level, null, null)], new Dictionary<int, int>());

    /// <summary>Builds one partner owning the fabricated forms.</summary>
    /// <param name="baseId">Base form id.</param>
    /// <param name="level">Partner level.</param>
    /// <param name="known">Acquired forms besides the base.</param>
    /// <param name="equipped">Equipped forms.</param>
    /// <returns>A persistent partner.</returns>
    public static PlayerSessionPartner Partner(int baseId, int level, IReadOnlyList<int>? known, IReadOnlyList<int>? equipped) => new(
        RewardSessionFixture.Seed(baseId),
        [baseId, .. known ?? EquippedForms],
        equipped ?? EquippedForms,
        [1, 2],
        [],
        new PartnerGrowthState { Level = level, MaxHp = 100, MaxMp = 10, BattleStats = [10, 10, 10, 10, 100, 5] });

    /// <summary>Builds a started production flow over the fabricated catalogue.</summary>
    /// <param name="session">Session reused by consecutive battles.</param>
    /// <param name="fighters">Optional replacement catalogue.</param>
    /// <returns>A session-backed flow.</returns>
    public static BattleFlowController Build(PlayerSession session, FighterData? fighters = null) =>
        GameHost.BattleSetupCoordinator.BuildForSession(
            new GameHost.BattleSetupCoordinator.BattleSessionBuildInput(
                fighters ?? Fighters(),
                Techniques(),
                RewardSessionFixture.Encounters(),
                1,
                session,
                RewardSessionFixture.Items(),
                RewardSessionFixture.Text(),
                new RewardSessionFixture.FixedRng()));

    /// <summary>Has the active partner act as each form in turn, then wins with the lethal technique.</summary>
    /// <param name="flow">Started flow.</param>
    /// <param name="forms">Forms to digivolve into, in order.</param>
    public static void FightAs(BattleFlowController flow, IReadOnlyList<int> forms)
    {
        foreach (int form in forms)
        {
            Digivolve(flow, form);
        }

        RewardSessionFixture.Win(flow);
    }

    /// <summary>Digivolves the active partner and finishes the enemy turn.</summary>
    /// <param name="flow">Started flow.</param>
    /// <param name="form">Equipped target form.</param>
    public static void Digivolve(BattleFlowController flow, int form)
    {
        if (!flow.Choose(BattleCommand.Digivolve, form, new RewardSessionFixture.FixedRng()))
        {
            throw new InvalidOperationException($"fixture digivolution to {form} was refused");
        }

        AdvanceEnemyTurn(flow);
    }

    /// <summary>Switches the active partner and finishes the enemy turn.</summary>
    /// <param name="flow">Started flow.</param>
    /// <param name="slot">Incoming party slot.</param>
    public static void Switch(BattleFlowController flow, int slot)
    {
        if (!flow.Choose(BattleCommand.Switch, slot, new RewardSessionFixture.FixedRng()))
        {
            throw new InvalidOperationException($"fixture switch to slot {slot} was refused");
        }

        AdvanceEnemyTurn(flow);
    }

    /// <summary>Builds the victory inputs.</summary>
    /// <param name="digimonExp">Encounter form experience.</param>
    /// <param name="tree">Optional first-partner evolution entries.</param>
    /// <param name="fighters">Optional replacement catalogue.</param>
    /// <returns>The production settlement context.</returns>
    public static BattleVictoryContext Victory(int digimonExp, IReadOnlyList<DigivolutionEntry>? tree = null, FighterData? fighters = null) => new(
        new BattleReward(DigimonExp: digimonExp, Exp: 0, Money: 0),
        1,
        fighters ?? Fighters(),
        RewardSessionFixture.GrowthTables(),
        new RewardSessionFixture.FixedRng(),
        id => BattleTextCatalog.Tokens([], $"fighter-{id}"),
        new BattleTextCatalog(RewardSessionFixture.Text()),
        Digivolutions: new DigivolutionData(
            [new PartnerDigivolution(0, 1, tree ?? []), new PartnerDigivolution(1, 2, []), new PartnerDigivolution(2, 3, [])],
            new DigivolutionBlast([])));

    /// <summary>Builds the fabricated catalogue: three bases, three equipped forms and one unlockable form.</summary>
    /// <param name="expLevel">ExpLevel of the equipped forms.</param>
    /// <param name="skillLevels">Acquisition levels of the equipped forms' skill entries.</param>
    /// <param name="knownLevels">Mastery levels of the equipped forms' skill entries.</param>
    /// <returns>The fighter catalogue.</returns>
    public static FighterData Fighters(int expLevel = 10, IReadOnlyList<int>? skillLevels = null, IReadOnlyList<int>? knownLevels = null)
    {
        DigimonData[] forms =
        [
            Form(1, [1, 2], [], [], 99),
            Form(2, [1, 2], [], [], 99),
            Form(3, [1, 2], [], [], 99),
            Form(5, [1, AcquiredTechnique, LateTechnique, 2], skillLevels ?? [2, 3, 99], knownLevels ?? [5, 99, 99], expLevel),
            Form(6, [1, AcquiredTechnique, LateTechnique, 2], skillLevels ?? [2, 3, 99], knownLevels ?? [5, 99, 99], expLevel),
            Form(7, [1, AcquiredTechnique, LateTechnique, 2], skillLevels ?? [2, 3, 99], knownLevels ?? [5, 99, 99], expLevel),
            Form(UnlockedForm, [1, 2], [], [], 99),
        ];
        return new FighterData(
            forms.ToDictionary(form => form.Id),
            forms,
            [new BattleTableEntry(100, 0, 1023, 100, [1, 0, 0], [1, 1, 1, 1, 1], new int[12], 0, [new BattleAction(1, 0, 0)], new BattleAction(0, 0, 0))]);
    }

    /// <summary>Builds the fabricated technique catalogue.</summary>
    /// <returns>Technique data including the form-learned techniques.</returns>
    public static TechniqueData Techniques() => new(new Dictionary<int, Dw3Production.Conversion.TechData>
    {
        [1] = Tech(0, 1),
        [2] = Tech(3, 1000),
        [AcquiredTechnique] = Tech(1, 2),
        [LateTechnique] = Tech(1, 2),
    });

    /// <summary>Finishes a pending enemy turn.</summary>
    /// <param name="flow">Flow with a possible enemy turn.</param>
    private static void AdvanceEnemyTurn(BattleFlowController flow)
    {
        while (flow.Phase == BattleFlowPhase.EnemyTurn)
        {
            flow.EnemyAct(new RewardSessionFixture.FixedRng());
        }
    }

    /// <summary>Builds one fabricated form record.</summary>
    /// <param name="id">Form id.</param>
    /// <param name="skills">Skill entries, entry zero being the basic attack.</param>
    /// <param name="skillLevels">Acquisition level of each entry after the first.</param>
    /// <param name="knownLevels">Mastery level of each entry after the first.</param>
    /// <param name="expLevel">Form level where the per-level requirement switches from 10 to 50.</param>
    /// <returns>A record accepted by the production battle factory.</returns>
    private static DigimonData Form(int id, IReadOnlyList<int> skills, IReadOnlyList<int> skillLevels, IReadOnlyList<int> knownLevels, int expLevel) => new(
        id, [10, 10, 10, 10, 100, 5], new int[7], skills, 0, new int[5], skillLevels, knownLevels, expLevel, 0, 10, 100, 10, 5, 1, new int[6], new int[7], [], id, 0, false);

    /// <summary>Builds a technique with deterministic accuracy.</summary>
    /// <param name="mp">MP cost.</param>
    /// <param name="power">Power.</param>
    /// <returns>Technique data.</returns>
    private static Dw3Production.Conversion.TechData Tech(int mp, int power) => new(0, mp, power, 0, 0, 128, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1);
}
