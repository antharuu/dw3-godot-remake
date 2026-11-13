using System.Text.Json;
using Dw3.Data;

namespace Dw3Production.Conversion;

/// <summary>Resolves bounded note/program/tempo cues into timed physical note transitions.</summary>
internal sealed class BattleSequenceCompiler
{
    private readonly DataPackReader _pack;
    private readonly JsonElement _bank;
    private readonly uint _id;
    private readonly int[] _programs = new int[16];
    private readonly List<AudioSequenceCommand> _commands = [];
    private double? _loopSeconds;

    /// <summary>Initializes a new instance of the <see cref="BattleSequenceCompiler"/> class.</summary>
    /// <param name="pack">Validated prepared pack.</param>
    /// <param name="bank">Prepared VAB attributes.</param>
    /// <param name="id">Original encoded cue identity.</param>
    public BattleSequenceCompiler(DataPackReader pack, JsonElement bank, uint id)
    {
        _pack = pack;
        _bank = bank;
        _id = id;
    }

    /// <summary>Validates the entire cue before any physical side effect.</summary>
    /// <param name="track">Prepared track events and timing.</param>
    /// <returns>Immutable resolved note schedule.</returns>
    public PreparedAudioSequence Compile(JsonElement track)
    {
        double resolution = track.GetProperty("ticksPerQuarterNote").GetDouble();
        double tempo = track.GetProperty("tempoMicrosecondsPerQuarterNote").GetDouble();
        if (resolution <= 0 || tempo <= 0)
        {
            throw new FormatException("Invalid prepared sequence timing.");
        }

        double seconds = 0;
        bool ended = false;
        foreach (var item in track.GetProperty("events").EnumerateArray())
        {
            int delta = item.GetProperty("DeltaTicks").GetInt32();
            if (delta < 0 || ended)
            {
                throw new FormatException("Invalid prepared sequence event order.");
            }

            seconds += delta * tempo / resolution / 1000000;
            int status = item.GetProperty("Status").GetInt32();
            int data1 = item.GetProperty("Data1").GetInt32();
            int data2 = item.GetProperty("Data2").GetInt32();
            if (status == 255)
            {
                ended = Metadata(data1, data2, ref tempo);
            }
            else
            {
                Channel(seconds, status, data1, data2);
            }
        }

        return ended ? new PreparedAudioSequence(_commands, seconds, _loopSeconds) : throw new FormatException("Sequence has no end marker.");
    }

    /// <summary>Handles only supported tempo and end markers, rejecting other metadata.</summary>
    /// <param name="type">Prepared metadata type.</param>
    /// <param name="value">Prepared metadata value.</param>
    /// <param name="tempo">Active microseconds per quarter note.</param>
    /// <returns>Whether the track ended.</returns>
    private static bool Metadata(int type, int value, ref double tempo)
    {
        if (type == 0x2F)
        {
            return true;
        }

        if (type != 0x51 || value <= 0)
        {
            throw new FormatException($"Unsupported required sequence metadata {type:X2}.");
        }

        tempo = value;
        return false;
    }

    /// <summary>Applies supported channel messages without silently ignoring other effects.</summary>
    /// <param name="seconds">Absolute authored time.</param>
    /// <param name="status">Channel status.</param>
    /// <param name="first">First data byte.</param>
    /// <param name="second">Second data byte.</param>
    private void Channel(double seconds, int status, int first, int second)
    {
        if (status is < 128 or >= 240 || first is < 0 or > 127 || second is < 0 or > 127)
        {
            throw new FormatException("Invalid prepared sequence channel event.");
        }

        int channel = status & 15;
        switch (status & 240)
        {
            case 0xB0:
                Control(seconds, first, second);
                break;
            case 0xC0:
                _programs[channel] = first;
                break;
            case 0x80:
                _commands.Add(new(seconds, channel, first, null));
                break;
            case 0x90:
                Note(seconds, channel, first, second);
                break;
            case 0xE0:
                PitchBend(seconds, channel, first, second);
                break;
            default:
                throw new FormatException($"Unsupported required sequence status {status:X2} for cue {_id:X8}.");
        }
    }

    /// <summary>Queues a channel-scoped 14-bit pitch bend for active and future physical voices.</summary>
    /// <param name="seconds">Absolute authored event time.</param>
    /// <param name="channel">Source MIDI channel.</param>
    /// <param name="leastSignificant">Least-significant seven-bit source value.</param>
    /// <param name="mostSignificant">Most-significant seven-bit source value.</param>
    private void PitchBend(double seconds, int channel, int leastSignificant, int mostSignificant)
    {
        int bend = (mostSignificant << 7) | leastSignificant;
        _commands.Add(new AudioSequenceCommand(seconds, channel, 0, null, bend));
    }

    /// <summary>Recognizes the source music loop marker while rejecting unqualified controllers.</summary>
    /// <param name="seconds">Absolute authored control time.</param>
    /// <param name="controller">Source controller number.</param>
    /// <param name="value">Source controller value.</param>
    private void Control(double seconds, int controller, int value)
    {
        if (controller == 99 && value == 20)
        {
            _loopSeconds = seconds;
            return;
        }

        if (controller is 99 or 98 or 6)
        {
            return;
        }

        throw new FormatException($"Unsupported required sequence controller {controller:X2} for cue {_id:X8}.");
    }

    /// <summary>Resolves every active VAB tone whose authored key range contains this note.</summary>
    /// <param name="seconds">Authored start time.</param>
    /// <param name="channel">MIDI channel.</param>
    /// <param name="note">Original MIDI note.</param>
    /// <param name="velocity">Original MIDI velocity.</param>
    private void Note(double seconds, int channel, int note, int velocity)
    {
        if (velocity == 0)
        {
            _commands.Add(new(seconds, channel, note, null));
            return;
        }

        var tones = _bank.GetProperty("tones").EnumerateArray().Where(row => row.GetProperty("ProgramIndex").GetInt32() == _programs[channel])
            .Where(row => row.GetProperty("Attributes").GetProperty("MinNote").GetInt32() <= note && row.GetProperty("Attributes").GetProperty("MaxNote").GetInt32() >= note).ToArray();
        if (tones.Length == 0)
        {
            throw new FormatException($"Sequence {_id:X8} has no tone for program {_programs[channel]} note {note}.");
        }

        foreach (var tone in tones)
        {
            uint key = 0x80040000U | ((uint)_programs[channel] << 11) | ((uint)tone.GetProperty("ToneIndex").GetInt32() << 7) | (uint)note;
            var entry = BattleActionAudioLoader.ResolveTone(_pack, _bank, key);
            _commands.Add(new(seconds, channel, note, entry with { EncodedId = _id, BankGain = entry.BankGain * velocity / 127f }));
        }
    }
}
