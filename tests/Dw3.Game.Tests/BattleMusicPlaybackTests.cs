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

    private sealed class RecordingBackend : IBattleAudioBackend
    {
        private readonly List<Handle> _handles = [];

        public int Played { get; private set; }

        public int Stopped { get; private set; }

        public IReadOnlyList<Handle> Handles => _handles;

        public IVoiceHandle Play(AudioProgramEntry entry)
        {
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
