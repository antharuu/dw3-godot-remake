namespace Dw3Production.Presentation;

/// <summary>
/// A fighter's entrance as the source battle plays it for a switch, a replacement after a knockout, the end of a
/// blast and a forced return: the camera fades to the entering side's fighter view, the opponent is hidden after
/// thirty frames, the entrance cue and a white flash start after sixty, the incoming model replaces the outgoing one
/// under full white in its entrance motion with the camera cut to its own view, the flash falls back, the view holds
/// for 120 frames, then the camera returns to the enemy view, the incoming model takes its normal or weak idle and the
/// opponent is shown again. Fighters 0x1D2 and 0x1D3 bring their own extras (<see cref="BattleEntranceExtras"/>).
/// One <see cref="Step"/> is one source time unit: the source counters add <c>GFX.getFrameTime</c>, which counts
/// 1/60 s whatever the video mode (a PAL vsync adds 1.2 units), so the fixed 60 Hz battle tick plays the same
/// durations as the original without drift.
/// </summary>
internal sealed class BattleEntranceSequence
{
    /// <summary>Source camera fade and lead-in before the flash, in frames.</summary>
    public const int LeadFrames = 60;

    /// <summary>Frame of the lead-in at which the opponent model is hidden.</summary>
    public const int HideOpponentFrame = 30;

    /// <summary>Rise and fall duration passed to the source white flash.</summary>
    public const int FlashFrames = 60;

    /// <summary>Ordinary hold on the incoming fighter's view after the flash ended, in frames.</summary>
    public const int HoldFrames = BattleEntranceExtras.DefaultHoldFrames;

    /// <summary>Source entrance motion of the incoming model.</summary>
    public const int EntranceMotion = 13;

    /// <summary>Decoded fighter camera of a player entrance.</summary>
    public const int PlayerViewIndex = 10;

    /// <summary>Decoded fighter camera of an enemy entrance.</summary>
    public const int EnemyViewIndex = 2;

    private readonly IBattleEntranceStage _stage;
    private readonly int _idleMotion;
    private readonly BattleEntranceExtras _extras;
    private BattleWhiteFlash? _flash;
    private int _counter;
    private bool _started;
    private bool _opponentHidden;
    private bool _ending;

    /// <summary>Initializes a new instance of the <see cref="BattleEntranceSequence"/> class.</summary>
    /// <param name="enemy">Whether the enemy side's fighter enters.</param>
    /// <param name="idleMotion">Idle given at the end, from <see cref="BattleWeakIdleRule"/> or zero.</param>
    /// <param name="stage">Physical scene operations.</param>
    /// <param name="extras">What the incoming fighter brings, <see cref="BattleEntranceExtras.None"/> when omitted.</param>
    /// <exception cref="ArgumentOutOfRangeException">The idle selection is neither zero nor one.</exception>
    public BattleEntranceSequence(bool enemy, int idleMotion, IBattleEntranceStage stage, BattleEntranceExtras? extras = null)
    {
        ArgumentNullException.ThrowIfNull(stage);
        if (idleMotion is not (BattleWeakIdleRule.Normal or BattleWeakIdleRule.Weak))
        {
            throw new ArgumentOutOfRangeException(nameof(idleMotion));
        }

        Enemy = enemy;
        _idleMotion = idleMotion;
        _stage = stage;
        _extras = extras ?? BattleEntranceExtras.None;
    }

    /// <summary>Gets a value indicating whether the enemy side's fighter enters.</summary>
    public bool Enemy { get; }

    /// <summary>Gets the current stage of the entrance.</summary>
    public BattleEntrancePhase Phase { get; private set; } = BattleEntrancePhase.Lead;

    /// <summary>Gets the number of source frames played so far.</summary>
    public int Frames { get; private set; }

    /// <summary>Gets a value indicating whether the incoming model already replaced the outgoing one.</summary>
    public bool Swapped { get; private set; }

    /// <summary>Gets a value indicating whether the entrance ended.</summary>
    public bool IsComplete => Phase == BattleEntrancePhase.Complete;

    /// <summary>Gets the additive white level drawn this frame.</summary>
    public int FlashLevel => _flash?.Level ?? 0;

    /// <summary>Gets the decoded fighter camera index of the entering side.</summary>
    private int ViewIndex => Enemy ? EnemyViewIndex : PlayerViewIndex;

    /// <summary>Plays one source frame. The flash child updates after the entrance in the same frame, so the entrance
    /// reacts to the flash state of the previous frame.</summary>
    public void Step()
    {
        if (IsComplete)
        {
            return;
        }

        Frames++;
        switch (Phase)
        {
            case BattleEntrancePhase.Lead:
                StepLead();
                break;
            case BattleEntrancePhase.FlashRising:
                StepFlashRising();
                break;
            case BattleEntrancePhase.FlashFalling:
                StepFlashFalling();
                break;
            default:
                StepHold();
                break;
        }

        _stage.PresentFlash(FlashLevel);
    }

    /// <summary>Starts the camera fade, hides the opponent at its frame and waits for the lead-in.</summary>
    private void StepLead()
    {
        if (!_started)
        {
            _started = true;
            if (_extras != BattleEntranceExtras.None)
            {
                _stage.PrepareEntranceExtras(_extras);
            }

            _stage.MoveCameraToFighter(Enemy, ViewIndex, LeadFrames);
        }

        _counter++;
        if (!_opponentHidden && _counter >= HideOpponentFrame)
        {
            _opponentHidden = true;
            _stage.SetOpponentVisible(false);
        }

        if (_counter >= LeadFrames)
        {
            Phase = BattleEntrancePhase.FlashRising;
        }
    }

    /// <summary>Plays the cue and creates the flash in the same source frame, then puts the incoming model in place once the flash held full white.</summary>
    private void StepFlashRising()
    {
        if (_flash is null)
        {
            _stage.BeginEntranceCue();
            _flash = new BattleWhiteFlash(FlashFrames);
        }
        else if (_flash.Phase == BattleWhiteFlashPhase.Holding)
        {
            _stage.SwapInModel(EntranceMotion);
            _stage.MoveCameraToFighter(Enemy, ViewIndex, 0);
            if (_extras != BattleEntranceExtras.None)
            {
                _stage.ApplyEntranceExtras(_extras);
            }

            Swapped = true;
            Phase = BattleEntrancePhase.FlashFalling;
        }

        _flash.Step();
    }

    /// <summary>Ends the flash, then starts the hold in the frame after the flash was removed.</summary>
    private void StepFlashFalling()
    {
        if (_flash is not { } flash)
        {
            return;
        }

        if (!_ending)
        {
            _ending = true;
            flash.End(FlashFrames);
        }
        else if (flash.Phase == BattleWhiteFlashPhase.Ended)
        {
            _flash = null;
            _counter = 0;
            Phase = BattleEntrancePhase.Hold;
            StepHold();
            return;
        }

        flash.Step();
    }

    /// <summary>Holds the incoming view, then returns the camera and gives the incoming model its idle.</summary>
    private void StepHold()
    {
        _counter++;
        if (_counter < _extras.HoldFrames)
        {
            return;
        }

        _stage.MoveCameraToEnemyView(Enemy ? 1 : LeadFrames);
        _stage.SetEnteringIdle(_idleMotion);
        _stage.SetOpponentVisible(true);
        Phase = BattleEntrancePhase.Complete;
    }
}
