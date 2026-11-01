namespace Dw3Production.Simulation;

/// <summary>Advances the animated battle reward report one display frame at a time.</summary>
public sealed class FightReportController
{
    private readonly FightReportInput _input;
    private readonly FightReportPartnerPanel[] _partners;
    private readonly List<string> _logs = [];
    private readonly FightReportFade _header = new();
    private readonly FightReportFade _footer = new();
    private readonly FightReportFade _exit = new();
    private readonly FightReportMessageBox _message;
    private FightReportPhase _phase;
    private int _time;
    private int _bgScroll;
    private bool _scrollSkip;
    private int _partnerIndex;
    private bool _done;

    /// <summary>Creates a report controller from one completed battle's reward data.</summary>
    /// <exception cref="ArgumentNullException">Thrown when the input or its party list is null.</exception>
    public FightReportController(FightReportInput input)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        ArgumentNullException.ThrowIfNull(input.Partners);
        _partners = [.. input.Partners.Select(partner => new FightReportPartnerPanel(partner))];
        _message = new FightReportMessageBox(input.MessageTokenCounts);
        View = BuildView();
    }

    /// <summary>Gets the state that should be drawn for the current display frame.</summary>
    public FightReportView View { get; private set; }

    /// <summary>Gets diagnostic entries emitted while unsupported report details are encountered.</summary>
    public IReadOnlyList<string> Logs => _logs;

    /// <summary>Calculates the decimal-wheel increment used by an animated experience counter.</summary>
    public static int RollStep(int targetExp)
    {
        if (targetExp <= 0)
        {
            return -954437177;
        }

        var value = targetExp;
        var result = 0;
        while (value > 0)
        {
            result = (result * 10) + 1;
            value /= 10;
        }

        return result;
    }

    /// <summary>Advances the report by one PAL frame and handles one Cross button edge.</summary>
    public void Step(bool crossPressedEdge)
    {
        if (_done)
        {
            return;
        }

        _time++;
        TickBackground();
        _message.Tick(crossPressedEdge);
        foreach (var partner in _partners)
        {
            partner.Tick(_time, _message, _logs);
        }

        _phase = NextPhase();
        View = BuildView();
    }

    /// <summary>Scrolls the background one pixel every other frame, wrapping at 96.</summary>
    private void TickBackground()
    {
        if (_scrollSkip)
        {
            _bgScroll = (_bgScroll + 1) % 96;
        }

        _scrollSkip = !_scrollSkip;
    }

    /// <summary>Runs the current report phase for one frame and returns the phase for the next frame.</summary>
    /// <returns>The phase for the next frame.</returns>
    private FightReportPhase NextPhase() => _phase switch
    {
        FightReportPhase.Initial => FightReportPhase.ShowPanels,
        FightReportPhase.ShowPanels => ShowPanels(),
        FightReportPhase.WaitPanels => WaitPanels(),
        FightReportPhase.FooterIn => _footer.Update() ? FightReportPhase.SelectPartner : _phase,
        FightReportPhase.SelectPartner => SelectPartner(),
        FightReportPhase.HeaderIn => HeaderIn(),
        FightReportPhase.ExpMessage => ExpMessage(),
        FightReportPhase.RaisePartner => RaisePartner(),
        FightReportPhase.HeaderOut => _header.Update() ? FightReportPhase.SelectPartner : _phase,
        FightReportPhase.Money => Money(),
        FightReportPhase.ItemStart => ItemStart(),
        FightReportPhase.ItemMessage => _message.TakeFinished() ? FightReportPhase.BeginExit : _phase,
        FightReportPhase.StartExit => FightReportPhase.BeginExit,
        FightReportPhase.BeginExit => StartExit(),
        FightReportPhase.ExitPanels => ExitPanels(),
        FightReportPhase.ExitFade => ExitFade(),
        _ => _phase,
    };

    /// <summary>Starts every partner panel's fade in.</summary>
    /// <returns>The panel wait phase.</returns>
    private FightReportPhase ShowPanels()
    {
        SetPartnerPhases(FightReportPartnerPhase.ShowStart);
        return FightReportPhase.WaitPanels;
    }

    /// <summary>Starts the footer's fade in once every panel is shown.</summary>
    /// <returns>The footer phase once every panel is idle, otherwise the current phase.</returns>
    private FightReportPhase WaitPanels()
    {
        if (!_partners.All(partner => partner.Phase == FightReportPartnerPhase.Idle))
        {
            return _phase;
        }

        _footer.Start(true, 10);
        return FightReportPhase.FooterIn;
    }

    /// <summary>
    /// Selects the next partner that earned experience and fades the header in for it; with no
    /// partner left, moves on to the money message.
    /// </summary>
    /// <returns>The header phase, or the money phase when no partner is left.</returns>
    private FightReportPhase SelectPartner()
    {
        while (_partnerIndex < _partners.Length && _partners[_partnerIndex].Source.ExpGained == 0)
        {
            _partnerIndex++;
        }

        if (_partnerIndex >= _partners.Length)
        {
            return FightReportPhase.Money;
        }

        _partners[_partnerIndex].Selected = true;
        _header.Start(true, 10);
        return FightReportPhase.HeaderIn;
    }

    /// <summary>Once the header is in, shows message 2 with the selected partner's experience gain.</summary>
    /// <returns>The experience message phase once the header is in, otherwise the current phase.</returns>
    private FightReportPhase HeaderIn()
    {
        if (!_header.Update())
        {
            return _phase;
        }

        _message.Start(2, _partners[_partnerIndex].Source.ExpGained);
        return FightReportPhase.ExpMessage;
    }

    /// <summary>Once the experience message is read, starts the selected panel's experience sequence.</summary>
    /// <returns>The raise phase once the message is read, otherwise the current phase.</returns>
    private FightReportPhase ExpMessage()
    {
        if (!_message.TakeFinished())
        {
            return _phase;
        }

        var partner = _partners[_partnerIndex];
        partner.ExpSequenceStarted = true;
        partner.Phase = FightReportPartnerPhase.SlotExp;
        return FightReportPhase.RaisePartner;
    }

    /// <summary>Once the selected panel is back to idle, fades the header out and moves to the next partner.</summary>
    /// <returns>The header-out phase once the panel is idle, otherwise the current phase.</returns>
    private FightReportPhase RaisePartner()
    {
        if (_partners[_partnerIndex].Phase != FightReportPartnerPhase.Idle)
        {
            return _phase;
        }

        _header.Start(false, 10);
        _partnerIndex++;
        return FightReportPhase.HeaderOut;
    }

    /// <summary>
    /// Shows message 10 with the money won, then, once it is read, the item message when the
    /// battle dropped an item (item 0 means none) or the exit.
    /// </summary>
    /// <returns>The item or exit phase once the money message is read, otherwise the current phase.</returns>
    private FightReportPhase Money()
    {
        if (_message.IsEmpty)
        {
            _message.Start(10, _input.Money);
            return _phase;
        }

        if (!_message.TakeFinished())
        {
            return _phase;
        }

        return _input.Item == 0 ? FightReportPhase.StartExit : FightReportPhase.ItemStart;
    }

    /// <summary>Shows message 11 with the dropped item.</summary>
    /// <returns>The item message phase.</returns>
    private FightReportPhase ItemStart()
    {
        _message.Start(11, _input.Item);
        return FightReportPhase.ItemMessage;
    }

    /// <summary>Starts the 30-frame exit fade and fades the footer and every panel out.</summary>
    /// <returns>The panel exit phase.</returns>
    private FightReportPhase StartExit()
    {
        _exit.Start(true, 30);
        _footer.Start(false, 10);
        SetPartnerPhases(FightReportPartnerPhase.HideStart);
        return FightReportPhase.ExitPanels;
    }

    /// <summary>Waits for the footer and every panel to be hidden.</summary>
    /// <returns>The exit fade phase once everything is hidden, otherwise the current phase.</returns>
    private FightReportPhase ExitPanels() =>
        _footer.Update() && _partners.All(partner => partner.Phase == FightReportPartnerPhase.Hidden)
            ? FightReportPhase.ExitFade
            : _phase;

    /// <summary>Ends the report once the exit fade is complete.</summary>
    /// <returns>The done phase once the fade is complete, otherwise the current phase.</returns>
    private FightReportPhase ExitFade()
    {
        if (!_exit.Update())
        {
            return _phase;
        }

        _done = true;
        return FightReportPhase.Done;
    }

    /// <summary>Puts every partner panel into <paramref name="phase"/>.</summary>
    /// <param name="phase">The phase every panel starts.</param>
    private void SetPartnerPhases(FightReportPartnerPhase phase)
    {
        foreach (var partner in _partners)
        {
            partner.Phase = phase;
        }
    }

    private FightReportView BuildView()
    {
        var partnerViews = _partners.Select(partner => partner.ToView()).ToArray();
        return new FightReportView(_header.Level, _footer.Level, _bgScroll, partnerViews, _message.ToView(_time), _exit.Level, _done);
    }
}
