using Dw3Production.Conversion;
using Godot;

namespace Dw3Production.Presentation;

/// <summary>
/// Physical consumer of the global SPU reverb state: a private mixer bus with a native reverb,
/// feeding the master bus, through which every reverb-enabled voice (VAB tone mode bit 4) plays.
/// Source semantics: libsnd keeps one global type and one depth, both changed by SEP data entries.
/// The game's sound start-up selects Studio B with depth zero, which is the initial state here.
/// SsUtSetReverbDepth scales 0-127 linearly to the SPU output volume, mapped to the linear wet level
/// with an untouched dry path. Type zero switches the reverb off.
/// Backend approximation: Godot's reverb is not the SPU reverb program. The room size follows the
/// documented SPU work-area size of each preset relative to the largest room preset (Space Echo),
/// so larger presets ring longer. SPU filter coefficients, the 22.05 kHz reverb rate and the exact
/// tail shape are not reproduced and are not claimed.
/// </summary>
internal sealed class GodotReverbBus : IDisposable
{
    /// <summary>libsnd SS_REV_TYPE_STUDIO_B, selected by the game's sound initialisation.</summary>
    internal const int InitialType = 3;

    private const string OutputBus = "Master";
    private const float SpaceEchoWorkArea = 0xF6C0;
    private static int _instances;
    private readonly AudioEffectReverb _effect;

    /// <summary>Initializes a new instance of the <see cref="GodotReverbBus"/> class.</summary>
    public GodotReverbBus()
    {
        Name = $"Dw3BattleReverb{Interlocked.Increment(ref _instances)}";
        int index = AudioServer.BusCount;
        AudioServer.AddBus(index);
        AudioServer.SetBusName(index, Name);
        AudioServer.SetBusSend(index, OutputBus);
        _effect = new AudioEffectReverb { Dry = 1, Wet = 0, PredelayMsec = 20, PredelayFeedback = 0, Hipass = 0, Spread = 1 };
        AudioServer.AddBusEffect(index, _effect);
        Configure();
    }

    /// <summary>Gets the unique mixer bus name for reverb-enabled voices.</summary>
    public string Name { get; }

    /// <summary>Gets the current source reverb preset.</summary>
    public int Type { get; private set; } = InitialType;

    /// <summary>Gets the current source reverb depth, 0-127.</summary>
    public int Depth { get; private set; }

    /// <summary>Gets the applied native wet level, for diagnostics.</summary>
    public float Wet => _effect.Wet;

    /// <summary>Applies one authored source change.</summary>
    /// <param name="change">Type or depth in source units.</param>
    public void Apply(AudioReverbChange change)
    {
        ArgumentNullException.ThrowIfNull(change);
        switch (change.Parameter)
        {
            case AudioReverbParameter.Type:
                _ = RoomSize(change.Value);
                Type = change.Value;
                break;
            case AudioReverbParameter.Depth when change.Value is >= 0 and <= 127:
                Depth = change.Value;
                break;
            default:
                throw new FormatException($"Invalid reverb change {change}.");
        }

        Configure();
        GD.Print($"battle-reverb type={Type} depth={Depth} room={_effect.RoomSize:0.000} wet={_effect.Wet:0.000} enabled={Type != 0}");
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        int index = AudioServer.GetBusIndex(Name);
        if (index > 0)
        {
            AudioServer.RemoveBus(index);
        }

        _effect.Dispose();
    }

    /// <summary>Maps a source preset to a native room size from its documented SPU work-area size.</summary>
    /// <param name="type">libsnd SS_REV_TYPE value (0-6 or 9).</param>
    /// <returns>Native room size in [0, 1].</returns>
    internal static float RoomSize(int type) => type switch
    {
        0 => 0,
        1 => 0x26C0 / SpaceEchoWorkArea,
        2 => 0x1F40 / SpaceEchoWorkArea,
        3 => 0x4840 / SpaceEchoWorkArea,
        4 => 0x6FE0 / SpaceEchoWorkArea,
        5 => 0xADE0 / SpaceEchoWorkArea,
        6 => 1,
        9 => 0x3C00 / SpaceEchoWorkArea,
        _ => throw new NotSupportedException($"Reverb preset {type} has no native mapping."),
    };

    /// <summary>Pushes the source state to the native effect, which reads it on every mix block.</summary>
    private void Configure()
    {
        _effect.RoomSize = RoomSize(Type);
        _effect.Wet = Type == 0 ? 0 : Depth / 127f;
        AudioServer.SetBusEffectEnabled(AudioServer.GetBusIndex(Name), 0, Type != 0);
    }
}
