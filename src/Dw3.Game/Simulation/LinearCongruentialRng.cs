using System.Globalization;
using System.Security.Cryptography;

namespace Dw3Production.Simulation;

/// <summary>
/// Deterministic random source for encounters and battles. Each value advances the linear congruential
/// recurrence <c>state = state * 1103515245 + 12345</c> (mod 2^32), the recurrence of the PlayStation
/// C library <c>rand()</c>, and keeps bits 16 to 27 of the new state, so every value lies in 0..4095.
/// The same seed always replays the same sequence.
/// </summary>
public sealed class LinearCongruentialRng : IEncounterRng, IBattleRng
{
    /// <summary>Multiplier of the recurrence.</summary>
    private const uint Multiplier = 1103515245;

    /// <summary>Increment of the recurrence.</summary>
    private const uint Increment = 12345;

    private uint _state;

    /// <summary>Initializes a new instance of the <see cref="LinearCongruentialRng"/> class.</summary>
    /// <param name="seed">The initial state; the first value is computed from the state after one step.</param>
    public LinearCongruentialRng(uint seed)
    {
        _state = seed;
    }

    /// <summary>
    /// Creates a generator from the text of the <c>DW3_RNG_SEED</c> environment variable: a decimal
    /// integer (negative values keep their 32-bit pattern) makes runs reproducible, and a missing or
    /// unparsable value draws the seed from the operating system's random source.
    /// </summary>
    /// <param name="seedText">The seed text, or <see langword="null"/> when the variable is unset.</param>
    /// <returns>A new generator.</returns>
    public static LinearCongruentialRng FromSeedText(string? seedText)
    {
        uint seed = int.TryParse(seedText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed)
            ? unchecked((uint)parsed)
            : (uint)RandomNumberGenerator.GetInt32(int.MaxValue);
        return new LinearCongruentialRng(seed);
    }

    /// <summary>Returns the next encounter value.</summary>
    /// <returns>A value in 0..4095.</returns>
    public int Draw() => Next();

    /// <summary>Returns the next battle value; callers reduce it with their own mask or modulo.</summary>
    /// <returns>A value in 0..4095.</returns>
    public int NextValue() => Next();

    /// <summary>Advances the recurrence one step.</summary>
    /// <returns>Bits 16 to 27 of the new state.</returns>
    private int Next()
    {
        _state = unchecked((_state * Multiplier) + Increment);
        return (int)((_state >> 16) & 0xFFF);
    }
}
