namespace Dw3Production;

/// <summary>Maps temporary fighter MP values to the decoded battle-menu digit labels.</summary>
public static class BattleMenuMpDigits
{
    /// <summary>Returns entry 0x1A below 100, 0x0F below 1000 and 0x24 at or above 1000.</summary>
    /// <param name="mp">Current fighter MP.</param>
    /// <returns>Decoded menu text entry id.</returns>
    public static int Entry(int mp)
    {
        if (mp < 100)
        {
            return 0x1A;
        }

        return mp < 1000 ? 0xF : 0x24;
    }
}
