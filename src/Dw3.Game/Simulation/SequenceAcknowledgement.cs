namespace Dw3Production.Simulation;

/// <summary>Distinguishes application from a physical wait and a missing consumer.</summary>
public enum SequenceAcknowledgement
{
    /// <summary>The consumer applied or deliberately accepted a qualified no-op.</summary>
    Applied,

    /// <summary>The consumer is still loading or waiting for a physical callback.</summary>
    Pending,

    /// <summary>No qualified consumer exists. Playback remains failed and recoverable, not successful.</summary>
    Unavailable,
}
