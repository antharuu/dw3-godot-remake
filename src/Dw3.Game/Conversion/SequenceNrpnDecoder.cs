namespace Dw3Production.Conversion;

/// <summary>
/// Interprets the SEP NRPN/data-entry controllers of one track (CC99 parameter, CC98 attribute,
/// CC6 value) with libsnd semantics. The state belongs to the sequence, not to a MIDI channel.
/// CC99 values 20 and 30 are the loop start and loop end markers; a loop start takes its repeat
/// count from the next data entry, where 127 means endless. CC99 values 0-16 select a tone (16 for
/// every tone of the program), CC98 selects the attribute, and a data entry applies it. Only the
/// global reverb type (15) and depth (16) attributes are qualified; any other attribute, a finite
/// loop count or an incomplete selection is refused instead of being ignored.
/// </summary>
internal sealed class SequenceNrpnDecoder
{
    private const int NrpnParameter = 99;
    private const int NrpnAttribute = 98;
    private const int DataEntry = 6;
    private const int AllTones = 16;
    private const int LoopStartMarker = 20;
    private const int LoopEndMarker = 30;
    private const int EndlessLoopCount = 127;
    private const int ReverbTypeAttribute = 15;
    private const int ReverbDepthAttribute = 16;

    private int? _tone;
    private int? _attribute;
    private bool _awaitingLoopCount;

    /// <summary>Gets the time of the first authored loop-start marker.</summary>
    public double? LoopStartSeconds { get; private set; }

    /// <summary>Gets the time of the first authored loop-end marker.</summary>
    public double? LoopEndSeconds { get; private set; }

    /// <summary>Gets a value indicating whether a controller number belongs to the NRPN triplet.</summary>
    /// <param name="controller">MIDI controller number.</param>
    /// <returns>True for CC99, CC98 and CC6.</returns>
    public static bool Handles(int controller) => controller is NrpnParameter or NrpnAttribute or DataEntry;

    /// <summary>Applies one controller in authored order.</summary>
    /// <param name="seconds">Absolute authored event time.</param>
    /// <param name="controller">CC99, CC98 or CC6.</param>
    /// <param name="value">Seven-bit controller value.</param>
    /// <returns>A global reverb change when the data entry completes one, otherwise null.</returns>
    public AudioReverbChange? Apply(double seconds, int controller, int value) => controller switch
    {
        NrpnParameter => Parameter(seconds, value),
        NrpnAttribute => Attribute(value),
        DataEntry => Entry(value),
        _ => throw new FormatException($"Controller {controller} is not an NRPN controller."),
    };

    /// <summary>Validates one reverb preset index against the presets this backend can represent.</summary>
    /// <param name="type">libsnd SS_REV_TYPE value.</param>
    /// <returns>The validated preset.</returns>
    private static int ReverbType(int type) => type is >= 0 and <= 6 or 9
        ? type
        : throw new FormatException($"Reverb preset {type} (echo/delay or unknown) is not qualified.");

    /// <summary>Handles CC99: loop markers or a tone selection that resets the attribute.</summary>
    /// <param name="seconds">Absolute authored event time.</param>
    /// <param name="value">Parameter value.</param>
    /// <returns>Always null, a parameter selection has no audible effect by itself.</returns>
    private AudioReverbChange? Parameter(double seconds, int value)
    {
        _attribute = null;
        _tone = null;
        _awaitingLoopCount = false;
        switch (value)
        {
            case LoopStartMarker:
                LoopStartSeconds ??= seconds;
                _awaitingLoopCount = true;
                break;
            case LoopEndMarker when LoopStartSeconds is null:
                throw new FormatException("Sequence loop end precedes its loop start.");
            case LoopEndMarker:
                LoopEndSeconds ??= seconds;
                break;
            case <= AllTones:
                _tone = value;
                break;
            default:
                throw new FormatException($"Unsupported NRPN parameter {value}.");
        }

        return null;
    }

    /// <summary>Handles CC98: selects the attribute of the already selected tone(s).</summary>
    /// <param name="value">libsnd DE_* attribute index.</param>
    /// <returns>Always null.</returns>
    private AudioReverbChange? Attribute(int value)
    {
        if (_tone is null)
        {
            throw new FormatException("NRPN attribute has no selected tone.");
        }

        _attribute = value;
        return null;
    }

    /// <summary>Handles CC6: a loop count or the value of the selected attribute.</summary>
    /// <param name="value">Data-entry value.</param>
    /// <returns>The resulting global reverb change, or null for a loop count.</returns>
    private AudioReverbChange? Entry(int value)
    {
        if (_awaitingLoopCount)
        {
            _awaitingLoopCount = false;
            return value == EndlessLoopCount ? null : throw new FormatException($"Finite sequence loop count {value} is not qualified.");
        }

        return _attribute switch
        {
            ReverbTypeAttribute => new AudioReverbChange(AudioReverbParameter.Type, ReverbType(value)),
            ReverbDepthAttribute => new AudioReverbChange(AudioReverbParameter.Depth, value),
            null => throw new FormatException("Data entry has no selected NRPN attribute."),
            _ => throw new FormatException($"Unsupported NRPN data-entry attribute {_attribute}."),
        };
    }
}
