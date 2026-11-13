namespace Dw3Production.Session;

/// <summary>Stores one acquired Digimon form's persistent experience, level and learned techniques.</summary>
public sealed class PartnerFormProgress
{
    /// <summary>Initializes a new instance of the <see cref="PartnerFormProgress"/> class.</summary>
    /// <param name="formId">The acquired Digimon form id.</param>
    /// <param name="level">The form's initial level.</param>
    /// <param name="exp">The form's initial experience.</param>
    /// <param name="techniques">Technique ids already known by this form.</param>
    public PartnerFormProgress(int formId, int level = 1, int exp = 0, IReadOnlyList<int>? techniques = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(formId);
        FormId = formId;
        Level = Math.Clamp(level, 1, 99);
        Exp = Math.Clamp(exp, 0, 9_999_999);
        Techniques = [.. techniques ?? []];
    }

    /// <summary>Gets the persistent Digimon form id.</summary>
    public int FormId { get; }

    /// <summary>Gets or sets the form level.</summary>
    public int Level { get; set; }

    /// <summary>Gets or sets the form experience.</summary>
    public int Exp { get; set; }

    /// <summary>Gets the techniques learned by this form.</summary>
    public List<int> Techniques { get; }
}
