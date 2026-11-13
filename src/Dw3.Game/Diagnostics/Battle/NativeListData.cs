using Dw3Production.Conversion;
using Dw3Production.Session;
using Dw3Production.Simulation;
using BattleEnemy = Dw3Production.Simulation.BattleEnemy;
using TechData = Dw3Production.Simulation.TechData;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Supplies prepared pack data to the native list proof, without replacing menu dispatch.</summary>
internal sealed class NativeListData
{
    private readonly TechniqueData _techs;

    /// <summary>Initializes a new instance of the <see cref="NativeListData"/> class.</summary>
    /// <param name="loader">Validated prepared pack reader.</param>
    public NativeListData(BattleDataPackLoader loader)
    {
        Fighters = loader.LoadFighters();
        Text = loader.LoadBattleText();
        Items = loader.LoadBattleItems();
        var catalog = new BattleTextCatalog(Text);
        var techs = loader.LoadTechs();
        _techs = techs;

        // A controlled learned pool combines real form skill records to exercise 6+1.
        // It is not a claim that the debug starter naturally owns seven techniques.
        Techniques = [.. Fighters.DigimonOrdered.SelectMany(form => BattleSetupFactory.PlayerTechniques(Fighters, techs, form.Id, catalog))
            .DistinctBy(tech => tech.Id).Where(tech => tech.Tech.Mp > 0 && tech.Tech.Power > 0).Take(7)];
        var seed = BattleSetupFactory.BuildParty(Fighters, 25, [Fighters.DigimonOrdered[0].Id])[0] ?? throw new InvalidOperationException("Prepared partner missing.");
        int[] forms = [.. Fighters.DigimonOrdered.Skip(8).Take(4).Select(form => form.Id)];
        Partner = new PlayerSessionPartner(seed, [seed.Id, .. forms], [.. forms.Take(3)], [.. Techniques.Select(tech => tech.Id)], []);
    }

    /// <summary>Gets the prepared fighter catalogue.</summary>
    public FighterData Fighters { get; }

    /// <summary>Gets the prepared text tables.</summary>
    public BattleTextData Text { get; }

    /// <summary>Gets the prepared item metadata.</summary>
    public BattleItemsData Items { get; }

    /// <summary>Gets the controlled seven-entry pool of unmodified pack techniques.</summary>
    public IReadOnlyList<BattleTechnique> Techniques { get; }

    /// <summary>Gets the controlled acquired/equipped state using real form identifiers.</summary>
    public PlayerSessionPartner Partner { get; }

    /// <summary>Gets the actual loaded technique used by item 0x5A.</summary>
    public TechData ItemTechnique => BattleSetupFactory.ToTech(_techs.Techs[0x89]);

    /// <summary>Gets the actual loaded item metadata for item 0x5A.</summary>
    public BattleItemsData ItemTechniqueItems => new([Items.Items.Single(item => item.Id == 0x5A)]);

    /// <summary>Creates a real flow with controlled resources and scheduler speed.</summary>
    /// <param name="pagedPool">Whether to install the controlled seven-entry stress pool after normal session setup.</param>
    /// <returns>The initialized battle flow.</returns>
    public BattleFlowController BuildFlow(bool pagedPool = true)
    {
        var setup = new BattleSetup();
        setup.Partners[0] = Partner.BattleSeed;
        Partner.BattleSeed.Stats.Stats[BattleStat.Speed] = 999;
        setup.Enemies[0] = new BattleEnemy { Fighter = 51, Hp = 30000, Mp = 100, Strength = 16, BaseStats = BattleSetupFactory.ToEnemyBaseStats(Fighters.BattleTable.First(entry => entry.Id == 51)) };
        var names = Fighters.BattleTable.ToDictionary(entry => entry.Id, entry => entry.NameId);
        foreach (var form in Fighters.DigimonOrdered)
        {
            names[form.Id] = form.NameId;
        }

        var flow = new BattleFlowController
        {
            Text = new BattleTextCatalog(Text),
            NameIds = names,
            EnemyBasicTech = new TechData { Accuracy = 128, Power = 1 },
            ItemStock = new Dictionary<int, int> { [42] = 2 },
        };
        SessionBattleCatalogFactory.ConfigureActivePartner(flow, new PlayerSession([Partner], flow.ItemStock), Fighters, _techs, Text);
        flow.Start(BattleState.InitFighters(setup, Partner.OriginFormId, new BattleAttackProofRng(0)));
        if (pagedPool)
        {
            flow.PlayerTechniques = Techniques;
        }

        return flow;
    }

    /// <summary>Builds a real prepared flow narrowed to item 0x5A without substituting its source technique.</summary>
    /// <returns>Started flow with the loaded item and technique catalogue.</returns>
    public BattleFlowController BuildItemTechniqueFlow()
    {
        BattleFlowController flow = BuildFlow(pagedPool: false);
        BattleItemData item = ItemTechniqueItems.Items[0];
        flow.Items = new Dictionary<int, BattleItem> { [item.Id] = BattleSetupFactory.ToItem(item) };
        flow.ItemStock = new Dictionary<int, int> { [item.Id] = 2 };
        flow.Techs = _techs.Techs.ToDictionary(pair => pair.Key, pair => BattleSetupFactory.ToTech(pair.Value));
        flow.State.Techs = flow.Techs;
        return flow;
    }
}
