namespace Dw3Production.Conversion;

/// <summary>Counts the glyphs a text window types for a resolved battle text line.</summary>
public static class BattleTextGlyphCount
{
    /// <summary>Text control command inserting the substitution named by its first argument.</summary>
    private const int SubstitutionCommand = 5;

    /// <summary>Counts the glyph and icon tokens of a line, with each substitution counted where it is inserted.</summary>
    /// <param name="line">Resolved line, possibly <see cref="BattleTextLine.Missing"/>.</param>
    /// <returns>The number of typed tokens, 0 for a missing line.</returns>
    public static int Count(BattleTextLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return Count(line.Tokens, line.Substitutions);
    }

    /// <summary>Counts typed tokens of one token stream.</summary>
    /// <param name="tokens">Token stream.</param>
    /// <param name="substitutions">Token streams inserted by substitution controls.</param>
    /// <returns>The number of typed tokens.</returns>
    private static int Count(IReadOnlyList<TextToken> tokens, IReadOnlyList<IReadOnlyList<TextToken>> substitutions)
    {
        var count = 0;
        foreach (TextToken token in tokens)
        {
            count += token.IsControl ? Inserted(token, substitutions) : 1;
        }

        return count;
    }

    /// <summary>Counts the typed tokens a control inserts.</summary>
    /// <param name="token">Control token.</param>
    /// <param name="substitutions">Token streams inserted by substitution controls.</param>
    /// <returns>The inserted substitution's typed tokens, 0 for any other control.</returns>
    private static int Inserted(TextToken token, IReadOnlyList<IReadOnlyList<TextToken>> substitutions)
    {
        if (token.Command != SubstitutionCommand || token.Args.Count == 0)
        {
            return 0;
        }

        var index = token.Args[0];
        return index > 0 && index <= substitutions.Count ? Count(substitutions[index - 1], []) : 0;
    }
}
