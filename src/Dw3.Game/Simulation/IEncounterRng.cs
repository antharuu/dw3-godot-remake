namespace Dw3Production.Simulation;

/// <summary>
/// The injectable source of random values for field encounters. <see cref="Draw"/> returns
/// the next RANDOM_TABLE entry, 0..RANDOM_COUNT-1 (0..4095), from a pseudo-random table of
/// 4096 entries. A deterministic implementation makes the controller testable bit-for-bit.
/// </summary>
public interface IEncounterRng
{
    /// <summary>Returns the next value from the encounter random table (0..4095).</summary>
    /// <returns>A value in the range 0..4095 taken from the RANDOM_TABLE sequence.</returns>
    int Draw();
}
