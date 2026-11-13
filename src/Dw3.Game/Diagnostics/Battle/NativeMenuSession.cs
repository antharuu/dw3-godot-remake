using Dw3Production.Conversion;
using Dw3Production.Simulation;
using Godot;
using BattleEnemy = Dw3Production.Simulation.BattleEnemy;
using TechData = Dw3Production.Simulation.TechData;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>Provides fabricated data to a native GameHost without booting a field or loading a pack.
/// Only data is fabricated. Input, focus, ticks and all command callbacks run production code.</summary>
internal sealed class NativeMenuSession : IDisposable
{
    private readonly GameHost _host;

    /// <summary>Initializes a new instance of the <see cref="NativeMenuSession"/> class.</summary>
    /// <param name="enemyFirst">Whether the synthetic enemy owns the opening turn.</param>
    /// <param name="includePartnerItems">Whether the fabricated source-shaped partner boost family is included in the item menu.</param>
    /// <param name="includeFieldItems">Whether the fabricated source-shaped field-element family is included in the item menu.</param>
    /// <param name="includeOffensiveItems">Whether the fabricated source-shaped enemy-targeted family is included in the item menu.</param>
    public NativeMenuSession(bool enemyFirst = false, bool includePartnerItems = false, bool includeFieldItems = false, bool includeOffensiveItems = false)
    {
        Flow = BuildFlow(enemyFirst, includePartnerItems, includeFieldItems, includeOffensiveItems);
        _host = new GameHost(
            Flow,
            new BattleTextData([], [], [], [], [], [], [], new Dictionary<string, BattleTextTemplate>(), new Dictionary<string, int>()),
            new BattleItemsData(ItemsData(includePartnerItems, includeFieldItems, includeOffensiveItems)),
            Rng);
        Menu.OpenRoot(Flow.RootMenuLines());
    }

    /// <summary>Initializes a new instance of the <see cref="NativeMenuSession"/> class from supplied data.</summary>
    /// <param name="flow">Initialized flow whose catalogues are exercised unchanged.</param>
    /// <param name="text">Prepared or fabricated text data.</param>
    /// <param name="items">Prepared or fabricated item data.</param>
    public NativeMenuSession(BattleFlowController flow, BattleTextData text, BattleItemsData items)
    {
        Flow = flow;
        _host = new GameHost(flow, text, items, Rng);
        Menu.OpenRoot(flow.RootMenuLines());
    }

    /// <summary>Gets the real flow installed in the host.</summary>
    public BattleFlowController Flow { get; }

    /// <summary>Gets the required active player for native assertions.</summary>
    public BattleFighter Player => Flow.State.ActivePlayerFighter ?? throw new InvalidOperationException("Native fixture player missing.");

    /// <summary>Gets the required active enemy for native assertions.</summary>
    public BattleFighter Enemy => Flow.State.ActiveEnemyFighter ?? throw new InvalidOperationException("Native fixture enemy missing.");

    /// <summary>Gets the required text catalogue for native assertions.</summary>
    public BattleTextCatalog Text => Flow.Text ?? throw new InvalidOperationException("Native fixture text missing.");

    /// <summary>Gets the counting simulation random source.</summary>
    public BattleAttackProofRng Rng { get; } = new(0);

    /// <summary>Gets the production host's menu, not a test-owned page.</summary>
    public BattleMenuController Menu => ((IBattleMenuDriverHost)_host).Menu;

    /// <summary>Gets the host's semantic state, for observation only.</summary>
    public InputState Input => ((IBattleMenuDriverHost)_host).Input;

    /// <summary>Dispatches through the same menu tick caller used by the battle scheduler.</summary>
    public void Tick() => ((IBattleTickHost)_host).TickBattleMenu();

    /// <summary>Injects a native key event through GameHost._Input, with no diagnostic input owner.</summary>
    /// <param name="key">Logical key.</param>
    /// <param name="pressed">Press or release.</param>
    /// <param name="echo">Whether this is an OS repeat.</param>
    /// <param name="tick">Whether to sample immediately or batch until the next event.</param>
    public void KeyEvent(Key key, bool pressed, bool echo = false, bool tick = true)
    {
        using var input = new InputEventKey { Keycode = key, Pressed = pressed, Echo = echo };
        _host._Input(input);
        if (tick)
        {
            Tick();
        }
    }

    /// <summary>Sends a complete key gesture, including its release tick.</summary>
    /// <param name="key">Logical key.</param>
    public void Tap(Key key)
    {
        KeyEvent(key, true);
        KeyEvent(key, false);
    }

    /// <summary>Invokes the real Godot notification override without changing any OS window.</summary>
    /// <param name="focused">Simulated native focus state.</param>
    public void Focus(bool focused) => _host._Notification((int)(focused ? Node.NotificationWMWindowFocusIn : Node.NotificationWMWindowFocusOut));

    /// <summary>Injects a controller event through the production host ingress.</summary>
    /// <param name="button">Controller button.</param>
    /// <param name="pressed">Press or release.</param>
    public void Button(JoyButton button, bool pressed)
    {
        using var input = new InputEventJoypadButton { ButtonIndex = button, Pressed = pressed, Device = 19 };
        _host._Input(input);
        Tick();
    }

    /// <summary>Injects a left-stick direction through the production host ingress.</summary>
    /// <param name="value">Native vertical axis value.</param>
    public void Stick(float value)
    {
        using var input = new InputEventJoypadMotion { Axis = JoyAxis.LeftY, AxisValue = value, Device = 19 };
        _host._Input(input);
        Tick();
    }

    /// <inheritdoc/>
    public void Dispose() => _host.Free();

    /// <summary>Creates a synthetic battle whose enemy survives repeated weak hits.</summary>
    /// <param name="enemyFirst">Whether the synthetic enemy owns the opening turn.</param>
    /// <param name="includePartnerItems">Whether the active-partner boost family belongs in the fabricated item menu.</param>
    /// <param name="includeFieldItems">Whether the field-element boost family belongs in the fabricated item menu.</param>
    /// <param name="includeOffensiveItems">Whether the enemy-targeted family belongs in the fabricated item menu.</param>
    /// <returns>Initialized real simulation.</returns>
    private static BattleFlowController BuildFlow(bool enemyFirst, bool includePartnerItems = false, bool includeFieldItems = false, bool includeOffensiveItems = false)
    {
        var setup = new BattleSetup();
        setup.Partners[0] = new PartnerSeed { Id = 1, Hp = 100, MaxHp = 100, Mp = 50, MaxMp = 50, Stats = Stats(enemyFirst ? 1 : 999) };
        setup.Partners[1] = new PartnerSeed { Id = 3, Hp = 100, MaxHp = 100, Mp = 50, MaxMp = 50, Stats = Stats(enemyFirst ? 1 : 999) };
        setup.Enemies[0] = new BattleEnemy { Fighter = 2, Hp = 999, Mp = 50, Strength = 16, BaseStats = Stats(enemyFirst ? 999 : 1) };
        var flow = new BattleFlowController
        {
            PlayerBasicTech = new TechData { Accuracy = 128, Power = 1 },
            EnemyBasicTech = new TechData { Accuracy = 128, Power = 1 },
            PlayerTechniques = [.. Enumerable.Range(7, 7).Select(id => new BattleTechnique(id, "synthetic", new TechData { Mp = 5 }))],
            PlayerDigivolutions = [1, 4],
            Items = Items(includePartnerItems, includeFieldItems, includeOffensiveItems),
            ItemStock = ItemStock(includePartnerItems, includeFieldItems, includeOffensiveItems),
            Techs = ItemTechniques(includeOffensiveItems),
        };
        flow.Start(BattleState.InitFighters(setup, 1, new BattleAttackProofRng(0)));
        return flow;
    }

    /// <summary>Builds combat stats with a controlled speed.</summary>
    /// <param name="speed">Turn ordering stat.</param>
    /// <returns>Fabricated stats.</returns>
    private static BattleStats Stats(int speed)
    {
        var stats = new BattleStats();
        stats.Stats[BattleStat.Attack] = 1;
        stats.Stats[BattleStat.Defense] = 999;
        stats.Stats[BattleStat.Speed] = speed;
        return stats;
    }

    /// <summary>Builds the fabricated item records that the production menu renders for a native proof.</summary>
    /// <param name="includePartnerItems">Whether the active-partner boost family is included.</param>
    /// <param name="includeFieldItems">Whether the field-element boost family is included.</param>
    /// <param name="includeOffensiveItems">Whether the enemy-targeted item family is included.</param>
    /// <returns>Fabricated source-shaped records corresponding to the flow catalogue.</returns>
    private static List<BattleItemData> ItemsData(bool includePartnerItems, bool includeFieldItems, bool includeOffensiveItems)
    {
        var items = new List<BattleItemData>
        {
            new(0x2B, 0, [], 0, 1, 500, 0, 0, 0, 7, "heal", "partner"),
            new(0x42, 0, [], 0, 0, 0, 0, 0, 0, 7, "cureStatus", "partner"),
        };
        AddPartnerItemData(items, includePartnerItems);
        AddFieldItemData(items, includeFieldItems);
        AddOffensiveItemData(items, includeOffensiveItems);
        return items;
    }

    /// <summary>Adds the enemy-targeted menu records required by the matching native proof.</summary>
    /// <param name="items">Mutable fixture item record list.</param>
    /// <param name="includeOffensiveItems">Whether the offensive items are included.</param>
    private static void AddOffensiveItemData(List<BattleItemData> items, bool includeOffensiveItems)
    {
        if (!includeOffensiveItems)
        {
            return;
        }

        items.Add(new(0x55, 0, [], 0, 0, 0, 0, 0, 0, 7, "drain", "enemy"));
        items.Add(new(0x56, 0, [], 0, 0, 96, 0, 0, 0, 7, "confuse", "enemy"));
        items.Add(new(0x57, 0, [], 0, 0, 0, 0, 0, 0, 7, "weakenDefense", "enemy"));
        items.Add(new(0x58, 0, [], 0, 0, 0, 0, 0, 0, 7, "lowerStat", "enemy"));
        items.Add(new(0x59, 0, [], 0, 0, 64, 0, 0, 0, 7, "lowerSpeed", "enemy"));
        items.Add(new(0x5A, 0, [], 0, 0, 0, 0, 0, 0, 7, "itemTech", "enemy"));
    }

    /// <summary>Adds the active-partner menu records required by the matching native proof.</summary>
    /// <param name="items">Mutable fixture item record list.</param>
    /// <param name="includePartnerItems">Whether the partner items are included.</param>
    private static void AddPartnerItemData(List<BattleItemData> items, bool includePartnerItems)
    {
        if (!includePartnerItems)
        {
            return;
        }

        items.Add(new(0x48, 0, [], 0, 0, 128, 0, 0, 0, 7, "boostSpeed", "partner"));
        items.Add(new(0x49, 0, [], 0, 0, 128, 0, 0, 0, 7, "boostAttack", "partner"));
        items.Add(new(0x4A, 0, [], 0, 0, 128, 0, 0, 0, 7, "boostDefense", "partner"));
        items.Add(new(0x4B, 0, [], 0, 0, 0, 0, 0, 0, 7, "special", "partner"));
        items.Add(new(0x4C, 0, [], 0, 0, 50, 0, 0, 0, 7, "fillGauge", "partner"));
    }

    /// <summary>Adds the field-element menu records required by the matching native proof.</summary>
    /// <param name="items">Mutable fixture item record list.</param>
    /// <param name="includeFieldItems">Whether the field items are included.</param>
    private static void AddFieldItemData(List<BattleItemData> items, bool includeFieldItems)
    {
        if (!includeFieldItems)
        {
            return;
        }

        items.Add(new(0x4D, 0, [], 0, 0, 0, 0, 0, 0, 7, "boostElement", "field"));
        items.Add(new(0x54, 0, [], 0, 0, 0, 0, 0, 0, 7, "boostRandomElement", "field"));
    }

    /// <summary>Builds the fabricated source-shaped menu catalogue for the selected native proof families.</summary>
    /// <param name="includePartnerItems">Whether to include the active-partner boost family.</param>
    /// <param name="includeFieldItems">Whether to include the field-element boost family.</param>
    /// <param name="includeOffensiveItems">Whether to include the enemy-targeted item family.</param>
    /// <returns>Item catalogue accepted by the production flow.</returns>
    private static Dictionary<int, BattleItem> Items(bool includePartnerItems, bool includeFieldItems, bool includeOffensiveItems)
    {
        var items = new Dictionary<int, BattleItem>
        {
            [0x2B] = new(0x2B, "heal", "partner", 500),
            [0x42] = new(0x42, "cureStatus", "partner", 0),
        };
        AddPartnerItems(items, includePartnerItems);
        AddFieldItems(items, includeFieldItems);
        AddOffensiveItems(items, includeOffensiveItems);
        return items;
    }

    /// <summary>Adds the supported enemy-targeted item family when its native proof needs it.</summary>
    /// <param name="items">Mutable fixture item catalogue.</param>
    /// <param name="includeOffensiveItems">Whether the offensive items are included.</param>
    private static void AddOffensiveItems(Dictionary<int, BattleItem> items, bool includeOffensiveItems)
    {
        if (!includeOffensiveItems)
        {
            return;
        }

        items[0x55] = new(0x55, "drain", "enemy", 0);
        items[0x56] = new(0x56, "confuse", "enemy", 96);
        items[0x57] = new(0x57, "weakenDefense", "enemy", 0);
        items[0x58] = new(0x58, "lowerStat", "enemy", 0);
        items[0x59] = new(0x59, "lowerSpeed", "enemy", 64);
        items[0x5A] = new(0x5A, "itemTech", "enemy", 0);
    }

    /// <summary>Builds the qualified source item technique required by the offensive item fixture.</summary>
    /// <param name="includeOffensiveItems">Whether the item technique must be available to the started flow.</param>
    /// <returns>The prepared technique catalogue required by item 0x5A.</returns>
    private static Dictionary<int, TechData> ItemTechniques(bool includeOffensiveItems) => includeOffensiveItems
        ? new Dictionary<int, TechData> { [0x89] = new TechData { Power = 20, Icon = BattleConst.TechMagic } }
        : [];

    /// <summary>Adds the supported active-partner item family when its native proof needs it.</summary>
    /// <param name="items">Mutable fixture item catalogue.</param>
    /// <param name="includePartnerItems">Whether the partner items are included.</param>
    private static void AddPartnerItems(Dictionary<int, BattleItem> items, bool includePartnerItems)
    {
        if (!includePartnerItems)
        {
            return;
        }

        items[0x48] = new(0x48, "boostSpeed", "partner", 128);
        items[0x49] = new(0x49, "boostAttack", "partner", 128);
        items[0x4A] = new(0x4A, "boostDefense", "partner", 128);
        items[0x4B] = new(0x4B, "special", "partner", 0);
        items[0x4C] = new(0x4C, "fillGauge", "partner", 50);
    }

    /// <summary>Adds the supported field-element item family when its native proof needs it.</summary>
    /// <param name="items">Mutable fixture item catalogue.</param>
    /// <param name="includeFieldItems">Whether the field items are included.</param>
    private static void AddFieldItems(Dictionary<int, BattleItem> items, bool includeFieldItems)
    {
        if (!includeFieldItems)
        {
            return;
        }

        items[0x4D] = new(0x4D, "boostElement", "field", 0);
        items[0x54] = new(0x54, "boostRandomElement", "field", 0);
    }

    /// <summary>Builds positive stock for every fabricated item selected by the native proof.</summary>
    /// <param name="includePartnerItems">Whether the active-partner boost family is included.</param>
    /// <param name="includeFieldItems">Whether the field-element boost family is included.</param>
    /// <param name="includeOffensiveItems">Whether the enemy-targeted item family is included.</param>
    /// <returns>Positive stock for every item shown in the proof menu.</returns>
    private static Dictionary<int, int> ItemStock(bool includePartnerItems, bool includeFieldItems, bool includeOffensiveItems) =>
        Items(includePartnerItems, includeFieldItems, includeOffensiveItems).Keys.ToDictionary(id => id, _ => 2);
}
