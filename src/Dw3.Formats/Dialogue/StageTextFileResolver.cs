using System.Globalization;

namespace Dw3.Formats.Dialogue;

/// <summary>
/// Resolves a stage text selector to the language copy stored in one country folder of the disc.
/// </summary>
/// <remarks>
/// The disc stores each text file as consecutive language copies; the engine adds the language global
/// to the selector. Rather than assuming the numeric value of a language, the resolver looks for the
/// unique copy in <c>selector .. selector + copies - 1</c> whose disc member lives in
/// <c>AAA/DAT/COUNTRY/&lt;folder&gt;/</c>, and reads the talk number from its <c>?STALKNN.BIN</c> name.
/// </remarks>
public sealed class StageTextFileResolver(DiscFileTable files, int languageCopies, string countryFolder)
{
    private const string TalkStem = "STALK";
    private const string TalkExtension = ".BIN";
    private readonly string _folderPrefix = $"AAA/DAT/COUNTRY/{countryFolder}/";

    /// <summary>Finds the sole country-specific talk file among the selector's consecutive language copies.</summary>
    /// <param name="selector">Base engine file id before the runtime language offset is added.</param>
    /// <returns>The matching file id, ISO path and two-digit talk index.</returns>
    /// <exception cref="FormatException">No copy, several copies, or a non-talk member matches.</exception>
    public StageTextFile Resolve(int selector)
    {
        if (selector < 0 || selector > files.Count - languageCopies)
        {
            throw new FormatException($"Text selector 0x{selector:X} leaves no room for {languageCopies} language copies in the file-sector table.");
        }

        var matches = new List<(int FileId, string Path)>();
        for (int copy = 0; copy < languageCopies; copy++)
        {
            string? path = files.PathOf(selector + copy);
            if (path is not null && path.StartsWith(_folderPrefix, StringComparison.Ordinal))
            {
                matches.Add((selector + copy, path));
            }
        }

        if (matches.Count != 1)
        {
            throw new FormatException($"Text selector 0x{selector:X} has {matches.Count} language copies under {_folderPrefix}, expected exactly one.");
        }

        (int fileId, string isoPath) = matches[0];
        return new StageTextFile(selector, fileId, isoPath, ParseTalkIndex(isoPath));
    }

    /// <summary>Extracts the two-digit talk ordinal from the terminal <c>?STALKNN.BIN</c> member name.</summary>
    /// <param name="isoPath">Full ISO member path previously selected for the configured country folder.</param>
    /// <returns>The decimal suffix between the fixed <c>STALK</c> stem and <c>.BIN</c> extension.</returns>
    private static int ParseTalkIndex(string isoPath)
    {
        string name = isoPath[(isoPath.LastIndexOf('/') + 1)..];

        // Member names are one country letter, "STALK", two digits and ".BIN".
        bool shaped = name.Length == 1 + TalkStem.Length + 2 + TalkExtension.Length
            && name.AsSpan(1, TalkStem.Length).SequenceEqual(TalkStem)
            && name.EndsWith(TalkExtension, StringComparison.Ordinal);
        if (!shaped || !int.TryParse(name.AsSpan(1 + TalkStem.Length, 2), NumberStyles.None, CultureInfo.InvariantCulture, out int index))
        {
            throw new FormatException($"Stage text member {isoPath} is not a ?STALKNN.BIN talk file.");
        }

        return index;
    }
}
