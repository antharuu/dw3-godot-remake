namespace Dw3Production.Presentation;

/// <summary>Comparable end-to-end motion evidence recorded at one presentation cadence.</summary>
/// <param name="Cadence">Presentation cadence label in frames per second.</param>
/// <param name="Samples">Published model states at identical source-frame checkpoints.</param>
public sealed record BattleMotionCadenceTrace(string Cadence, IReadOnlyList<BattleMotionCadenceSample> Samples)
{
    /// <summary>Returns the first state field that differs between two equal-time production traces.</summary>
    /// <param name="expected">Reference trace, normally 60 FPS.</param>
    /// <param name="actual">Trace rendered at another presentation cadence.</param>
    /// <returns>Null when every publication and effective bone pose is identical, otherwise a stable field path.</returns>
    public static string? Compare(BattleMotionCadenceTrace expected, BattleMotionCadenceTrace actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);
        if (expected.Samples.Count != actual.Samples.Count)
        {
            return "samples.count";
        }

        for (var index = 0; index < expected.Samples.Count; index++)
        {
            var difference = CompareSample(expected.Samples[index], actual.Samples[index]);
            if (difference is not null)
            {
                return $"samples[{index}].{difference}";
            }
        }

        return null;
    }

    /// <summary>Compares all production-visible state for one elapsed source-frame checkpoint.</summary>
    /// <param name="expected">Reference model state.</param>
    /// <param name="actual">Candidate model state.</param>
    /// <returns>Null when equal, otherwise the first differing member path.</returns>
    private static string? CompareSample(BattleMotionCadenceSample expected, BattleMotionCadenceSample actual)
    {
        var values = new (string Name, int Expected, int Actual)[]
        {
            ("sourceFrame", expected.SourceFrame, actual.SourceFrame),
            ("motion", expected.Motion, actual.Motion),
            ("keyframe", expected.Keyframe, actual.Keyframe),
            ("frame", expected.Frame, actual.Frame),
            ("blend", expected.Blend, actual.Blend),
            ("blendTarget", expected.BlendTarget, actual.BlendTarget),
            ("blendSource", expected.BlendSource, actual.BlendSource),
        };
        foreach (var (name, left, right) in values)
        {
            if (left != right)
            {
                return name;
            }
        }

        if (expected.Blending != actual.Blending)
        {
            return "blending";
        }

        if (expected.Bones.Count != actual.Bones.Count)
        {
            return "bones.count";
        }

        for (var index = 0; index < expected.Bones.Count; index++)
        {
            var difference = CompareBone(expected.Bones[index], actual.Bones[index]);
            if (difference is not null)
            {
                return $"bones[{index}].{difference}";
            }
        }

        return null;
    }

    /// <summary>Compares one effective source-bone transform without an arbitrary floating-point tolerance.</summary>
    /// <param name="expected">Reference bone transform.</param>
    /// <param name="actual">Candidate bone transform.</param>
    /// <returns>Null when equal, otherwise the first differing member path.</returns>
    private static string? CompareBone(BattleMotionCadenceBone expected, BattleMotionCadenceBone actual)
    {
        if (expected.Index != actual.Index)
        {
            return "index";
        }

        if (expected.Visible != actual.Visible)
        {
            return "visible";
        }

        var values = new (string Name, int Expected, int Actual)[]
        {
            ("positionX", FloatBits(expected.OriginX), FloatBits(actual.OriginX)),
            ("positionY", FloatBits(expected.OriginY), FloatBits(actual.OriginY)),
            ("positionZ", FloatBits(expected.OriginZ), FloatBits(actual.OriginZ)),
            ("basisXx", FloatBits(expected.BasisXx), FloatBits(actual.BasisXx)),
            ("basisXy", FloatBits(expected.BasisXy), FloatBits(actual.BasisXy)),
            ("basisXz", FloatBits(expected.BasisXz), FloatBits(actual.BasisXz)),
            ("basisYx", FloatBits(expected.BasisYx), FloatBits(actual.BasisYx)),
            ("basisYy", FloatBits(expected.BasisYy), FloatBits(actual.BasisYy)),
            ("basisYz", FloatBits(expected.BasisYz), FloatBits(actual.BasisYz)),
            ("basisZx", FloatBits(expected.BasisZx), FloatBits(actual.BasisZx)),
            ("basisZy", FloatBits(expected.BasisZy), FloatBits(actual.BasisZy)),
            ("basisZz", FloatBits(expected.BasisZz), FloatBits(actual.BasisZz)),
        };
        foreach (var (name, left, right) in values)
        {
            if (left != right)
            {
                return name;
            }
        }

        return null;
    }

    /// <summary>Maps a floating-point component to its exact IEEE-754 bit representation for deterministic trace comparison.</summary>
    /// <param name="value">Transform component to preserve exactly.</param>
    /// <returns>The raw single-precision bit pattern.</returns>
    private static int FloatBits(float value) => BitConverter.SingleToInt32Bits(value);
}
