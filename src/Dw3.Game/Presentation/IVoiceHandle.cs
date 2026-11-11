namespace Dw3Production.Presentation;

/// <summary>An opaque handle to one active audio voice started by <see cref="IBattleAudioBackend.Play"/>.</summary>
public interface IVoiceHandle
{
    /// <summary>Gets a value indicating whether the voice has finished playing naturally.</summary>
    bool IsComplete { get; }
}
