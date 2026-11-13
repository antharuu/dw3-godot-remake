using Dw3Production.Session;

namespace Dw3Production.Simulation;

/// <summary>
/// The original report's form-experience rules: the share each used form receives and the form level curve.
/// </summary>
public static class FormExpRules
{
    /// <summary>The highest form level.</summary>
    public const int MaxLevel = 99;

    /// <summary>The highest stored form experience.</summary>
    public const int MaxExp = 9_999_999;

    /// <summary>
    /// Returns one used form's share: ten times the reward over the partner level (a fifth of the reward past
    /// level 50), kept whole for one used slot, 6/10 of it for two and split evenly for more, then held to at
    /// least 1 and at most 10, or 50 once the form has reached its ExpLevel.
    /// </summary>
    /// <param name="award">Encounter reward, partner level before its own experience and global used slots.</param>
    /// <param name="formLevel">The form's level before this grant.</param>
    /// <param name="expLevel">The form's decoded ExpLevel.</param>
    /// <returns>The experience the form receives.</returns>
    public static int Share(FormExpAward award, int formLevel, int expLevel)
    {
        ArgumentNullException.ThrowIfNull(award);
        int scaled = award.PartnerLevel < 51 ? award.DigimonExp * 10 / Math.Max(1, award.PartnerLevel) : award.DigimonExp / 5;
        int share = award.GlobalUsed switch
        {
            <= 1 => scaled,
            2 => scaled * 6 / 10,
            _ => scaled / award.GlobalUsed,
        };
        if (share <= 0)
        {
            return 1;
        }

        return Math.Min(share, formLevel < expLevel ? 10 : 50);
    }

    /// <summary>Returns the cumulative experience a form needs to leave its current level.</summary>
    /// <param name="level">Current form level.</param>
    /// <param name="expLevel">Decoded ExpLevel: ten experience a level up to it, fifty past it.</param>
    /// <returns>The cumulative requirement.</returns>
    public static int RequiredExp(int level, int expLevel)
    {
        if (expLevel >= level + 1)
        {
            return level * 10;
        }

        int below = expLevel - 1;
        return (below * 10) + ((level - below) * 50);
    }

    /// <summary>Adds experience to a form entry and raises its level as far as the experience reaches.</summary>
    /// <param name="progress">Mutable form entry.</param>
    /// <param name="exp">Experience to add.</param>
    /// <param name="expLevel">Decoded ExpLevel of the form.</param>
    /// <returns>True when at least one level was gained.</returns>
    public static bool AddExp(PartnerFormProgress progress, int exp, int expLevel)
    {
        ArgumentNullException.ThrowIfNull(progress);
        progress.Exp = Math.Min(MaxExp, progress.Exp + Math.Max(0, exp));
        bool leveled = false;
        while (progress.Level < MaxLevel && progress.Exp >= RequiredExp(progress.Level, expLevel))
        {
            progress.Level++;
            leveled = true;
        }

        return leveled;
    }
}
