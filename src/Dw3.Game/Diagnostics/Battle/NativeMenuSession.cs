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
    public NativeMenuSession(bool enemyFirst = false)
    {
        Flow = BuildFlow(enemyFirst);
        _host = new GameHost(
            Flow,
            new BattleTextData([], [], [], [], [], [], [], new Dictionary<string, BattleTextTemplate>(), new Dictionary<string, int>()),
            new BattleItemsData(
            [
                new BattleItemData(0x2B, 0, [], 0, 1, 500, 0, 0, 0, 7, "heal", "partner"),
                new BattleItemData(0x42, 0, [], 0, 0, 0, 0, 0, 0, 7, "cureStatus", "partner"),
            ]),
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
    /// <returns>Initialized real simulation.</returns>
    private static BattleFlowController BuildFlow(bool enemyFirst)
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
            Items = new Dictionary<int, BattleItem>
            {
                [0x2B] = new BattleItem(0x2B, "heal", "partner", 500),
                [0x42] = new BattleItem(0x42, "cureStatus", "partner", 0),
            },
            ItemStock = new Dictionary<int, int> { [0x2B] = 2, [0x42] = 2 },
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
}
