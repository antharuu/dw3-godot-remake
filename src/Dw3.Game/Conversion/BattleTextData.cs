namespace Dw3Production.Conversion;

/// <summary>The m6-battle-text-v2 contract: French token streams from MFIGHT, DIGNAM, SKLNAM and FGTRPT.</summary>
public sealed record BattleTextData(IReadOnlyList<IReadOnlyList<TextToken>> Menu, IReadOnlyList<IReadOnlyList<TextToken>> DigimonNames, IReadOnlyList<IReadOnlyList<TextToken>> TechniqueNames, IReadOnlyList<IReadOnlyList<TextToken>> SkillInfo, IReadOnlyList<IReadOnlyList<TextToken>> ItemInfo, IReadOnlyList<IReadOnlyList<TextToken>> Report, IReadOnlyList<BattleTextTemplate> Commands, IReadOnlyDictionary<string, BattleTextTemplate> Messages, IReadOnlyDictionary<string, int> ReportTemplates);
