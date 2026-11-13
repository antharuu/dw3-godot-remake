namespace Dw3Production.Presentation;

/// <summary>Integer 44.1 kHz ADSR rate accumulator from the documented SPU register contract.</summary>
internal static class SpuEnvelopeRate
{
    /// <summary>Advances one envelope sample without floating-point rate drift.</summary>
    /// <param name="counter">Persistent 15-bit fractional rate counter.</param>
    /// <param name="level">Current nonnegative amplitude.</param>
    /// <param name="shift">Register shift value.</param>
    /// <param name="step">Register step value.</param>
    /// <param name="decrease">Whether the phase decreases.</param>
    /// <param name="exponential">Whether the phase uses exponential stepping.</param>
    /// <returns>The saturated envelope level.</returns>
    public static int Advance(ref int counter, int level, int shift, int step, bool decrease, bool exponential)
    {
        int amount = decrease ? ~(7 - step) : 7 - step;
        amount <<= Math.Max(0, 11 - shift);
        int increment = 32768 >> Math.Max(0, shift - 11);
        if (exponential && !decrease && level > 0x6000)
        {
            amount >>= shift switch { < 10 => 2, 10 => 1, _ => 0 };
            increment >>= shift switch { >= 11 => 2, 10 => 1, _ => 0 };
        }

        if (exponential && decrease)
        {
            amount = (amount * level) >> 15;
        }

        if ((shift << 2 | step) != 127)
        {
            increment = Math.Max(increment, 1);
        }

        counter += increment;
        if ((counter & 32768) == 0)
        {
            return level;
        }

        counter &= 32767;
        return Math.Clamp(level + amount, 0, 32767);
    }
}
