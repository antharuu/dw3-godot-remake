namespace Dw3Production.Conversion;

/// <summary>One entry of the Digimon table: a Digimon's base battle stats, resistances, learned skills and growth.</summary>
public sealed record DigimonData(int Id, IReadOnlyList<int> BattleStats, IReadOnlyList<int> Resistances, IReadOnlyList<int> Skills, int PairTech, IReadOnlyList<int> StatusResists, IReadOnlyList<int> SkillLevels, IReadOnlyList<int> KnownLevels, int ExpLevel, int PairPartner, int ExpRate, int Hp, int Mp, int HpGrowth, int MpGrowth, IReadOnlyList<int> StatGrowth, IReadOnlyList<int> ResistGrowth, IReadOnlyList<int> BlastForms, int NameId, int Family, bool Placeholder);
