using Dw3Production.Conversion;
using Dw3Production.Presentation;
using Xunit;

namespace Dw3.Game.Tests;

public sealed class BattleAudioEnvelopeTests
{
    [Fact]
    public void Envelope_Attacks_Decays_ThenReleasesToZero()
    {
        var envelope = new SpuEnvelope(0x000F, 0x0000);
        Assert.Equal(0, envelope.Level);
        envelope.AdvanceSamples(8);
        Assert.True(envelope.Level > 0);
        envelope.AdvanceSamples(128);
        Assert.Equal(32767, envelope.Level);
        envelope.KeyOff();
        envelope.AdvanceSamples(128);
        Assert.Equal(0, envelope.Level);
        Assert.True(envelope.IsComplete);
    }

    [Fact]
    public void Release_AllOnesRateHoldsTheCurrentLevel()
    {
        var envelope = new SpuEnvelope(0x000F, 0x001F);
        envelope.AdvanceSamples(128);
        int before = envelope.Level;
        envelope.KeyOff();
        envelope.AdvanceSamples(44100);
        Assert.Equal(before, envelope.Level);
    }

    [Fact]
    public void EqualPriority_EvictsQuieterEnvelopeRatherThanOldestVoice()
    {
        var first = new Voice(0.9f);
        var second = new Voice(0.1f);
        var queue = new Queue<IVoiceHandle>([first, second, new Voice(0.5f)]);
        var stopped = new List<IVoiceHandle>();
        var pool = new BattleAudioVoicePool(_ => queue.Dequeue(), stopped.Add, 2);
        var entry = new AudioProgramEntry(0, "prepared.wav", 127, 64, 60, 0);
        pool.Play(entry);
        pool.Play(entry);
        pool.Play(entry);
        Assert.Same(second, Assert.Single(stopped));
    }

    private sealed class Voice(float envelope) : IVoiceHandle
    {
        public bool IsComplete => false;

        public float EnvelopeLevel => envelope;
    }
}
