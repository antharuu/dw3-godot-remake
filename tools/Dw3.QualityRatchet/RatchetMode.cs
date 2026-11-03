namespace Dw3.QualityRatchet;

/// <summary>
/// The commands of the quality ratchet.
/// </summary>
internal enum RatchetMode
{
    /// <summary>
    /// Fails when a count went up or a library is below the coverage minimum.
    /// </summary>
    Check,

    /// <summary>
    /// Writes the lower counts and higher coverage of the last measurement into the baseline.
    /// </summary>
    Update,

    /// <summary>
    /// Creates the baseline from the current measurement when none exists.
    /// </summary>
    Init,
}
