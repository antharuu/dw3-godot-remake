namespace Dw3Production.Presentation;

/// <summary>
/// Expands a fighter's motion steps and advances the resulting animation timeline.
/// </summary>
public sealed class BattleMotionMachine
{
    private const int BlendFlag = 0x8000;
    private const int ValueMask = 0xFFFF;
    private const int FinishMarker = 0xFFFF;
    private const int StepEndMarker = 0x7FFF;
    private const int BlendScale = 4096;

    private readonly IReadOnlyList<IReadOnlyList<IReadOnlyList<int>>> _motions;
    private readonly int[] _idleFrames;
    private readonly List<int> _keyframes = [];
    private readonly List<int> _targets = [];
    private readonly List<int> _sources = [];

    /// <summary>
    /// Initializes a new instance of the <see cref="BattleMotionMachine"/> class from decoded fighter motions and activates the selected idle motion.
    /// </summary>
    /// <param name="motions">Motions containing ordered animation steps.</param>
    /// <param name="idleMotion">Idle selection, normally zero or one.</param>
    public BattleMotionMachine(
        IReadOnlyList<IReadOnlyList<IReadOnlyList<int>>> motions,
        int idleMotion = 0)
    {
        ArgumentNullException.ThrowIfNull(motions);
        _motions = motions;
        _idleFrames = [ReadInitialFrame(0), ReadInitialFrame(1)];

        Motion = 1;
        IdleMotion = idleMotion;
        Keyframe = 1;
        BlendSource = -1;
        SetMotion(unchecked(idleMotion + 1), restart: true);
    }

    /// <summary>Gets the active one-based motion number.</summary>
    public int Motion { get; private set; }

    /// <summary>Gets the idle selection, where zero and one select the first two motions.</summary>
    public int IdleMotion { get; private set; }

    /// <summary>Gets the cursor into <see cref="Keyframes"/>.</summary>
    public int Keyframe { get; private set; }

    /// <summary>Gets the animation frame most recently published by <see cref="Step"/>.</summary>
    public int Frame { get; private set; }

    /// <summary>Gets the most recently published fixed-point blend weight.</summary>
    public int Blend { get; private set; }

    /// <summary>Gets a value indicating whether the published entry interpolates between two poses.</summary>
    public bool Blending { get; private set; }

    /// <summary>Gets a value indicating whether the active motion reached its terminal marker.</summary>
    public bool MotionDone { get; private set; }

    /// <summary>Gets a value indicating whether a completed motion returns to the selected idle motion.</summary>
    public bool ToIdle { get; private set; }

    /// <summary>Gets the destination frame of the current blend, or zero when none is active.</summary>
    public int BlendTarget { get; private set; }

    /// <summary>
    /// Gets the current blend source frame. A value of 65535 selects the displayed pose and
    /// negative one means that no blend source has been selected.
    /// </summary>
    public int BlendSource { get; private set; }

    /// <summary>Gets the expanded values of the active motion timeline.</summary>
    public IReadOnlyList<int> Keyframes => _keyframes;

    /// <summary>Gets a value indicating whether the expanded motion ends by jumping to an earlier frame.</summary>
    public bool Looping => _keyframes.Count > 0 && _keyframes[^1] == BlendFlag;

    /// <summary>
    /// Activates and expands a one-based motion unless the same motion is already active and a
    /// restart was not requested.
    /// </summary>
    /// <param name="motion">One-based motion number.</param>
    /// <param name="restart">Whether to rebuild an already active motion.</param>
    public void SetMotion(int motion, bool restart)
    {
        if (!restart && motion == Motion)
        {
            return;
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(motion, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(motion, _motions.Count);

        Motion = motion;
        if (motion == 1)
        {
            IdleMotion = 0;
        }
        else if (motion == 2)
        {
            IdleMotion = 1;
        }

        Keyframe = 1;
        MotionDone = false;
        ToIdle = false;
        Blending = false;
        BlendTarget = 0;
        BlendSource = -1;
        _keyframes.Clear();
        _targets.Clear();
        _sources.Clear();

        Expand(_motions[motion - 1]);
        if (_keyframes.Count == 0)
        {
            throw new InvalidOperationException("Motion expands to an empty animation timeline.");
        }
    }

    /// <summary>Publishes the current timeline entry and advances by the requested frame count.</summary>
    /// <param name="framesElapsed">Non-negative number of animation frames to advance.</param>
    public void Step(int framesElapsed)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(framesElapsed);

        if (MotionDone)
        {
            Blending = false;
            BlendTarget = 0;
            if (ToIdle)
            {
                SetMotion(unchecked(IdleMotion + 1), restart: false);
            }

            return;
        }

        PublishEntry(Keyframe);

        Keyframe = Math.Min(unchecked(Keyframe + framesElapsed), _keyframes.Count - 1);
        var landedValue = _keyframes[Keyframe];
        if (landedValue == BlendFlag)
        {
            Keyframe = _targets[Keyframe];
        }
        else if (landedValue == FinishMarker)
        {
            MotionDone = true;
        }
    }

    /// <summary>Changes which of the first two motions is used as the idle motion.</summary>
    /// <param name="idleMotion">Zero for the first idle motion or one for the second.</param>
    public void SetIdleMotion(int idleMotion)
    {
        if (idleMotion is not (0 or 1))
        {
            throw new ArgumentOutOfRangeException(nameof(idleMotion));
        }

        IdleMotion = idleMotion;
    }

    /// <summary>Reads the initial frame from the first step of the motion at <paramref name="motionIndex"/>, or zero when absent.</summary>
    /// <param name="motionIndex">Zero-based index into the motions list.</param>
    /// <returns>The first step's frame value, or zero when the motion or its first step is absent.</returns>
    private int ReadInitialFrame(int motionIndex)
    {
        if (motionIndex >= _motions.Count || _motions[motionIndex].Count == 0)
        {
            return 0;
        }

        return _motions[motionIndex][0][2];
    }

    /// <summary>Walks the decoded step table and appends keyframes, targets, and sources to the expanded timeline.</summary>
    /// <param name="steps">Decoded motion step rows from the fighter's motion data.</param>
    private void Expand(IReadOnlyList<IReadOnlyList<int>> steps)
    {
        for (var stepIndex = 0; stepIndex < steps.Count; stepIndex++)
        {
            var step = steps[stepIndex];
            var index = step[0];
            if (index == StepEndMarker)
            {
                break;
            }

            var count = step[1];
            if (count == 0)
            {
                AddEntry(step[2] & ValueMask, step[3], -1);
                break;
            }

            if (ExpandFrameRun(step, count))
            {
                continue;
            }

            ExpandBlendRun(step, count, stepIndex, steps);
        }
    }

    /// <summary>Appends count identical frame entries when step[3] is nonzero (sequential source frames).</summary>
    /// <param name="step">Decoded step row: [index, count, firstFrame, endFrame].</param>
    /// <param name="count">Number of frames to append.</param>
    /// <returns><see langword="true"/> when the run was appended; <see langword="false"/> when step[3] is zero.</returns>
    private bool ExpandFrameRun(IReadOnlyList<int> step, int count)
    {
        var firstFrame = step[2];
        var endFrame = step[3];
        if (endFrame == 0)
        {
            return false;
        }

        for (var offset = 0; offset < count; offset++)
        {
            AddEntry(unchecked(firstFrame + offset), 0, -1);
        }

        return true;
    }

    /// <summary>Expands a timed interpolation step into fixed-point blend entries.</summary>
    /// <param name="step">Current decoded step row.</param>
    /// <param name="count">Number of interpolation frames.</param>
    /// <param name="stepIndex">Zero-based position of <paramref name="step"/> in <paramref name="steps"/>.</param>
    /// <param name="steps">All decoded step rows needed to find neighboring pose frames.</param>
    private void ExpandBlendRun(IReadOnlyList<int> step, int count, int stepIndex, IReadOnlyList<IReadOnlyList<int>> steps)
    {
        var index = step[0];
        var target = steps[stepIndex + 1][2];
        if (target == -1)
        {
            target = _idleFrames[IdleMotion];
            ToIdle = true;
        }

        var source = index == 0 ? FinishMarker : steps[stepIndex - 1][3];
        for (var offset = 1; offset <= count; offset++)
        {
            var weighted = unchecked(offset * BlendScale) / unchecked(count + 1);
            AddEntry(weighted | BlendFlag, target, source);
        }
    }

    /// <summary>Appends one encoded timeline entry and its blend endpoints in matching list positions.</summary>
    /// <param name="value">Frame number or fixed-point blend value with its marker bit.</param>
    /// <param name="target">Destination pose frame for a blend entry.</param>
    /// <param name="source">Source pose frame for a blend entry, or negative one when absent.</param>
    private void AddEntry(int value, int target, int source)
    {
        _keyframes.Add(value);
        _targets.Add(target);
        _sources.Add(source);
    }

    /// <summary>Publishes one expanded entry as a direct frame or a blend state.</summary>
    /// <param name="index">Zero-based index into the expanded timeline.</param>
    private void PublishEntry(int index)
    {
        var value = _keyframes[index];
        if ((value & BlendFlag) != 0)
        {
            PublishBlendEntry(index, value);
            return;
        }

        BlendTarget = 0;
        BlendSource = -1;
        Frame = value;
        Blending = false;
    }

    /// <summary>Publishes the endpoints and 12-bit weight of an interpolation entry.</summary>
    /// <param name="index">Zero-based index into the blend endpoint lists.</param>
    /// <param name="value">Encoded timeline value containing the blend marker and weight.</param>
    private void PublishBlendEntry(int index, int value)
    {
        var target = _targets[index];
        if (BlendTarget != target)
        {
            BlendTarget = target;
            BlendSource = _sources[index];
        }

        Frame = target;
        Blend = value & 0x7FFF;
        Blending = true;
    }
}
