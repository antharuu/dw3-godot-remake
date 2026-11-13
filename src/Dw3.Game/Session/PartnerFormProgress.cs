namespace Dw3Production.Session;

/// <summary>
/// Stores one acquired Digimon form's persistent experience, level and six skill entries. An entry is first
/// acquired, which lets this form use the skill, and may later be mastered, which is a separate flag.
/// </summary>
public sealed class PartnerFormProgress
{
    /// <summary>The number of skill entries a form owns.</summary>
    public const int SkillEntryCount = 6;

    /// <summary>The number of leading skill entries that can be mastered. The sixth entry never can.</summary>
    public const int MasterableEntryCount = 5;

    private readonly int[] _skills = new int[SkillEntryCount];
    private readonly bool[] _mastered = new bool[SkillEntryCount];

    /// <summary>Initializes a new instance of the <see cref="PartnerFormProgress"/> class.</summary>
    /// <param name="formId">The acquired Digimon form id.</param>
    /// <param name="level">The form's initial level.</param>
    /// <param name="exp">The form's initial experience.</param>
    public PartnerFormProgress(int formId, int level = 1, int exp = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(formId);
        FormId = formId;
        Level = Math.Clamp(level, 1, 99);
        Exp = Math.Clamp(exp, 0, 9_999_999);
    }

    /// <summary>Gets the persistent Digimon form id.</summary>
    public int FormId { get; }

    /// <summary>Gets or sets the form level.</summary>
    public int Level { get; set; }

    /// <summary>Gets or sets the form experience.</summary>
    public int Exp { get; set; }

    /// <summary>Gets the six skill entries, each a technique id or zero while the entry is empty.</summary>
    public IReadOnlyList<int> SkillEntries => _skills;

    /// <summary>Gets the acquired technique ids in entry order. Only this form uses them in battle.</summary>
    public IReadOnlyList<int> Techniques => [.. _skills.Where(skill => skill != 0)];

    /// <summary>Gets the mastered technique ids in entry order.</summary>
    public IReadOnlyList<int> MasteredTechniques => [.. _skills.Where((skill, index) => skill != 0 && _mastered[index])];

    /// <summary>Tests whether one skill entry holds a mastered skill.</summary>
    /// <param name="index">Skill entry index.</param>
    /// <returns>True when the entry is mastered.</returns>
    public bool IsMastered(int index) => index >= 0 && index < SkillEntryCount && _mastered[index];

    /// <summary>Fills one empty skill entry.</summary>
    /// <param name="index">Skill entry index.</param>
    /// <param name="techniqueId">Acquired technique id.</param>
    /// <returns>True when the entry was empty and now holds the technique.</returns>
    public bool AcquireSkill(int index, int techniqueId)
    {
        if (index < 0 || index >= SkillEntryCount || techniqueId <= 0 || _skills[index] != 0)
        {
            return false;
        }

        _skills[index] = techniqueId;
        return true;
    }

    /// <summary>Masters one acquired, not yet mastered entry among the masterable ones.</summary>
    /// <param name="index">Skill entry index.</param>
    /// <returns>True when the entry became mastered.</returns>
    public bool MasterSkill(int index)
    {
        if (index < 0 || index >= MasterableEntryCount || _skills[index] == 0 || _mastered[index])
        {
            return false;
        }

        _mastered[index] = true;
        return true;
    }
}
