using Dw3Production.Conversion;
using Dw3Production.Presentation;
using Dw3Production.Simulation;
using Xunit;

namespace Dw3.Game.Tests;

public sealed class BattleSequenceAudioTests
{
    [Fact]
    public void Sequence_SchedulesRealNotes_KeysOff_DeduplicatesRetries_AndCancels()
    {
        var entry = new AudioProgramEntry(4, "prepared.wav", 127, 64, 60, 0);
        var sequence = new PreparedAudioSequence(
            [new(0.1, 15, 60, entry), new(0.2, 15, 60, null), new(0.3, 15, 62, entry with { Note = 62 })], 0.5);
        var backend = new Backend();
        var consumer = new BattleAudioConsumer(
            new Dictionary<int, AudioProgramEntry>(),
            backend,
            new Dictionary<int, PreparedAudioSequence> { [9] = sequence });
        var request = new SoundRequestEvent(BattleSide.Player, 9, 0);
        Assert.Equal(SequenceAcknowledgement.Applied, consumer.Consume(request));
        consumer.Consume(request);
        consumer.Advance(5);
        Assert.Empty(backend.Started);
        consumer.Advance(1);
        Assert.Single(backend.Started);
        consumer.Advance(6);
        Assert.Single(backend.Stopped);
        consumer.Advance(6);
        Assert.Equal(2, backend.Started.Count);
        consumer.Finish(false);
        consumer.StopAll();
        consumer.Advance(100);
        Assert.Equal(2, backend.Stopped.Count);
        Assert.Equal(2, backend.Started.Count);
    }

    [Fact]
    public void SeparateEqualSequenceRequests_BothStart_WhileRetryStartsOnlyOnce()
    {
        var entry = new AudioProgramEntry(4, "prepared.wav", 127, 64, 60, 0);
        var backend = new Backend();
        var consumer = new BattleAudioConsumer(new Dictionary<int, AudioProgramEntry>(), backend, new Dictionary<int, PreparedAudioSequence>
        {
            [9] = new([new(0, 0, 60, entry)], 1),
        });
        var request = new SoundRequestEvent(BattleSide.Player, 9, 0);
        consumer.Consume(request);
        consumer.Consume(request);
        consumer.Consume(new SoundRequestEvent(BattleSide.Player, 9, 0));
        Assert.Equal(2, backend.Started.Count);
        consumer.StopAll();
        Assert.Equal(2, backend.Stopped.Count);
    }

    [Fact]
    public void InvalidPreparedSample_FailsBeforeAcknowledgement_AndTheSameRequestCanRecover()
    {
        var entry = new AudioProgramEntry(4, "prepared.wav", 127, 64, 60, 0);
        var backend = new Backend { RejectPreparation = true };
        var consumer = new BattleAudioConsumer(new Dictionary<int, AudioProgramEntry>(), backend, new Dictionary<int, PreparedAudioSequence>
        {
            [9] = new([new(0.1, 0, 60, entry)], 1),
        });
        var request = new SoundRequestEvent(BattleSide.Player, 9, 0);
        Assert.Throws<FormatException>(() => consumer.Consume(request));
        Assert.Empty(backend.Started);
        backend.RejectPreparation = false;
        Assert.Equal(SequenceAcknowledgement.Applied, consumer.Consume(request));
        consumer.Consume(request);
        consumer.Advance(6);
        Assert.Single(backend.Started);
    }

    private sealed class Backend : IBattleAudioBackend
    {
        public List<AudioProgramEntry> Started { get; } = [];

        public List<IVoiceHandle> Stopped { get; } = [];

        public bool RejectPreparation { get; set; }

        public void Prepare(AudioProgramEntry entry)
        {
            if (RejectPreparation)
            {
                throw new FormatException("Invalid fabricated prepared PCM.");
            }
        }

        public IVoiceHandle Play(AudioProgramEntry entry)
        {
            Started.Add(entry);
            return new Voice();
        }

        public void StopVoice(IVoiceHandle handle) => Stopped.Add(handle);
    }

    private sealed class Voice : IVoiceHandle
    {
        public bool IsComplete => false;
    }
}
