namespace Dw3Production.Conversion;

/// <summary>The full dialogue runtime contract: zones -> actors -> talks, indexed text streams
/// and the font.</summary>
public sealed record DialogueData(IReadOnlyDictionary<string, ZoneDialogue> Zones, IReadOnlyDictionary<int, IReadOnlyList<IReadOnlyList<TextToken>>> Texts, FontData Font);
