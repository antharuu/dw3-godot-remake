using Dw3Production.Conversion;
using Dw3Production.Presentation;
using Xunit;

namespace Dw3.Game.Tests;

public sealed class BattleMusicPlaybackTests
{
    [Fact]
    public void Advance_PitchBend_UpdatesTheActivePhysicalVoice()
    {
        var entry = new AudioProgramEntry(1, "fixture.wav", 127, 64, 60, 0) { PitchBendUp = 12, PitchBendDown = 12 };
        var sequence = new PreparedAudioSequence(
            [
                new AudioSequenceCommand(0, 0, 60, entry),
                new AudioSequenceCommand(0.1, 0, 0, null, PitchBend: 16383),
            ],
            0.2);
        var backend = new RecordingBackend();
        var playback = new BattleSequenceVoice(sequence, backend);

        playback.Advance(6);

        Assert.Equal(12, Assert.Single(backend.Handles).PitchBendSemitones, 3);
    }

    [Fact]
    public void Advance_RepeatsFromAuthoredLoopMarkerTwiceWithoutStackingTheIntro()
    {
        var entry = new AudioProgramEntry(1, "fixture.wav", 127, 64, 60, 0) { PitchBendUp = 12, PitchBendDown = 12 };
        var sequence = new PreparedAudioSequence(
            [
                new AudioSequenceCommand(0, 0, 60, entry),
                new AudioSequenceCommand(0.05, 0, 0, null, PitchBend: 16383),
                new AudioSequenceCommand(0.1, 0, 60, null),
                new AudioSequenceCommand(0.15, 0, 61, entry),
                new AudioSequenceCommand(0.19, 0, 61, null),
            ],
            0.2,
            0.1);
        var backend = new RecordingBackend();
        var playback = new BattleMusicPlayback(sequence, backend);

        playback.Advance(12);
        playback.Advance(3);
        playback.Advance(6);

        Assert.True(playback.IsLooping);
        Assert.Equal(2, playback.LoopCount);
        Assert.Equal(3, backend.Played);
        Assert.Equal(3, backend.Stopped);
        Assert.All(backend.Handles, handle => Assert.Equal(12, handle.PitchBendSemitones, 3));
    }

    [Fact]
    public void Advance_ReverbControls_ReachThePhysicalBackendInAuthoredOrderOnlyOnceAcrossLoops()
    {
        var entry = new AudioProgramEntry(1, "fixture.wav", 127, 64, 60, 0) { Reverb = true };
        var sequence = new PreparedAudioSequence(
            [
                new AudioSequenceCommand(0, 0, 0, null, Reverb: new AudioReverbChange(AudioReverbParameter.Type, 3)),
                new AudioSequenceCommand(0.01, 0, 0, null, Reverb: new AudioReverbChange(AudioReverbParameter.Depth, 56)),
                new AudioSequenceCommand(0.05, 0, 60, entry),
                new AudioSequenceCommand(0.1, 0, 60, null),
            ],
            0.2,
            0.03,
            0.15);
        var backend = new RecordingBackend();
        var playback = new BattleMusicPlayback(sequence, backend);

        playback.Advance(10);
        playback.Advance(7);
        playback.Advance(7);

        Assert.Equal(2, playback.LoopCount);
        Assert.Equal(["reverb Type 3", "reverb Depth 56", "play", "play"], backend.Operations);
    }

    [Fact]
    public void Advance_LoopEndMarker_JumpsBeforeTheEndOfTrack()
    {
        var entry = new AudioProgramEntry(1, "fixture.wav", 127, 64, 60, 0);
        var sequence = new PreparedAudioSequence(
            [
                new AudioSequenceCommand(0.05, 0, 60, entry),
                new AudioSequenceCommand(0.18, 0, 61, entry),
            ],
            0.2,
            0.05,
            0.1);
        var backend = new RecordingBackend();
        var playback = new BattleMusicPlayback(sequence, backend);

        playback.Advance(7);

        Assert.Equal(1, playback.LoopCount);
        Assert.Equal(2, backend.Played);
    }

    [Fact]
    public void Advance_NoteBeforeAnyBend_PlaysAtTheMidiCentre()
    {
        var entry = new AudioProgramEntry(1, "fixture.wav", 127, 64, 60, 0) { PitchBendUp = 12, PitchBendDown = 12 };
        var backend = new RecordingBackend();

        _ = new BattleSequenceVoice(new PreparedAudioSequence([new AudioSequenceCommand(0, 0, 60, entry)], 0.1), backend);

        Assert.Equal(0, Assert.Single(backend.Handles).PitchBendSemitones, 3);
    }

    [Fact]
    public void Advance_ReverbWithoutAPhysicalConsumer_FailsInsteadOfBeingDropped()
    {
        var sequence = new PreparedAudioSequence(
            [new AudioSequenceCommand(0, 0, 0, null, Reverb: new AudioReverbChange(AudioReverbParameter.Depth, 56))],
            0.1);

        Assert.Throws<NotSupportedException>(() => new BattleSequenceVoice(sequence, new NoReverbBackend()));
    }

    private sealed class NoReverbBackend : IBattleAudioBackend
    {
        public IVoiceHandle Play(AudioProgramEntry entry) => new Handle();

        public void StopVoice(IVoiceHandle handle)
        {
        }
    }

    private sealed class RecordingBackend : IBattleAudioBackend
    {
        private readonly List<Handle> _handles = [];
        private readonly List<string> _operations = [];

        public IReadOnlyList<string> Operations => _operations;

        public int Played { get; private set; }

        public int Stopped { get; private set; }

        public IReadOnlyList<Handle> Handles => _handles;

        public void ApplyReverb(AudioReverbChange change) => _operations.Add($"reverb {change.Parameter} {change.Value}");

        public IVoiceHandle Play(AudioProgramEntry entry)
        {
            _operations.Add("play");
            Played++;
            var handle = new Handle();
            _handles.Add(handle);
            return handle;
        }

        public void StopVoice(IVoiceHandle handle)
        {
            Stopped++;
        }
    }

    private sealed class Handle : IVoiceHandle
    {
        public bool IsComplete => false;

        public float PitchBendSemitones { get; private set; }

        public void SetPitchBend(float semitones) => PitchBendSemitones = semitones;
    }
}
