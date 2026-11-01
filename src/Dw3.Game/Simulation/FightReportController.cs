namespace Dw3Production.Simulation;

/// <summary>Describes one party member's reward state after a battle.</summary>
public sealed record FightReportPartner(
    int PartyIndex,
    int PartnerId,
    int FighterId,
    int LevelBefore,
    int ExpBefore,
    int ExpGained,
    int LevelAfter,
    IReadOnlyList<int> SlotDigimonIds);

/// <summary>Supplies the rewards and party data displayed by a battle report.</summary>
public sealed record FightReportInput(
    IReadOnlyList<FightReportPartner> Partners,
    int Money,
    int Item,
    IReadOnlyDictionary<int, int>? MessageTokenCounts = null);

/// <summary>Describes a visible Digimon slot in a party member's report panel.</summary>
public sealed record FightReportSlotView(int DigimonId, bool Visible, bool ExpApplied, int BlinkFrame);

/// <summary>Describes the visible state of one party member's report panel.</summary>
public sealed record FightReportPartnerView(
    int PartyIndex,
    int PartnerId,
    int FighterId,
    int Fade,
    bool Selected,
    int CursorFrame,
    int LevelBlinkFrame,
    int ShownExp,
    int AnimationFrame,
    IReadOnlyList<FightReportSlotView> Slots);

/// <summary>Describes the message currently displayed by the battle report.</summary>
public sealed record FightReportMessage(
    int Entry,
    IReadOnlyList<int> Substitutions,
    int TokensRevealed,
    int TokenCount,
    bool ArrowVisible,
    int ArrowFrame);

/// <summary>Provides a complete frame snapshot for the battle reward report.</summary>
public sealed record FightReportView(
    int HeaderFade,
    int FooterFade,
    int BgScroll,
    IReadOnlyList<FightReportPartnerView> Partners,
    FightReportMessage? Message,
    int ExitFade,
    bool Done);

/// <summary>Advances the animated battle reward report one display frame at a time.</summary>
public sealed class FightReportController
{
    private const int FullFade = 4096;
    private readonly FightReportInput _input;
    private readonly PartnerState[] _partners;
    private readonly List<string> _logs = [];
    private readonly Fade _header = new();
    private readonly Fade _footer = new();
    private readonly Fade _exit = new();
    private ReportPhase _phase;
    private MessageState? _message;
    private int _time;
    private int _bgScroll;
    private bool _scrollSkip;
    private int _partnerIndex;
    private bool _slotWarningLogged;
    private bool _done;

    /// <summary>Creates a report controller from one completed battle's reward data.</summary>
    /// <exception cref="ArgumentNullException">Thrown when the input or its party list is null.</exception>
    public FightReportController(FightReportInput input)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        ArgumentNullException.ThrowIfNull(input.Partners);
        _partners = [.. input.Partners.Select(partner => new PartnerState(partner))];
        View = BuildView();
    }

    /// <summary>Gets the state that should be drawn for the current display frame.</summary>
    public FightReportView View { get; private set; }

    /// <summary>Gets diagnostic entries emitted while unsupported report details are encountered.</summary>
    public IReadOnlyList<string> Logs => _logs;

    /// <summary>Calculates the decimal-wheel increment used by an animated experience counter.</summary>
    public static int RollStep(int targetExp)
    {
        if (targetExp <= 0) return -954437177;
        var value = targetExp;

        var result = 0;
        while (value > 0)
        {
            result = result * 10 + 1;
            value /= 10;
        }
        return result;
    }

    /// <summary>Advances the report by one PAL frame and handles one Cross button edge.</summary>
    public void Step(bool crossPressedEdge)
    {
        if (_done) return;

        _time++;
        TickBackground();
        TickMessage(crossPressedEdge);
        foreach (var partner in _partners) TickPartner(partner);
        TickReport();
        View = BuildView();
    }

    private void TickBackground()
    {
        if (_scrollSkip)
        {
            _bgScroll = (_bgScroll + 1) % 96;
            _scrollSkip = false;
        }
        else _scrollSkip = true;
    }

    private void TickMessage(bool crossPressedEdge)
    {
        if (_message is not { Finished: false } message) return;
        if (message.Waiting)
        {
            if (crossPressedEdge) message.Finished = true;
            return;
        }

        if (crossPressedEdge)
        {
            message.Revealed = message.TokenCount;
            message.Waiting = true;
            return;
        }

        if (++message.RevealTimer > 6)
        {
            message.RevealTimer = 0;
            message.Revealed++;
            if (message.Revealed >= message.TokenCount) message.Waiting = true;
        }
    }

    private void TickPartner(PartnerState partner)
    {
        if (partner.Selected && _time % 5 == 0) partner.CursorFrame = (partner.CursorFrame + 1) % 4;
        if (partner.LevelBlink && _time % 3 == 0) partner.LevelBlinkFrame = Math.Min(10, partner.LevelBlinkFrame + 1);
        if (partner.Fade.Level != 0 && _time % 13 == 0) partner.AnimationFrame++;

        if (partner.ExpSequenceStarted && partner.Phase != PartnerPhase.Hidden) RollExp(partner);

        switch (partner.Phase)
        {
            case PartnerPhase.ShowStart:
                partner.Fade.Start(true, 10);
                partner.Phase = PartnerPhase.ShowFade;
                break;
            case PartnerPhase.ShowFade:
                if (partner.Fade.Update()) partner.Phase = PartnerPhase.Idle;
                break;
            case PartnerPhase.HideStart:
                partner.Fade.Start(false, 10);
                partner.Phase = PartnerPhase.HideFade;
                break;
            case PartnerPhase.HideFade:
                if (partner.Fade.Update()) partner.Phase = PartnerPhase.Hidden;
                break;
            case PartnerPhase.SlotExp:
                if (!_slotWarningLogged)
                {
                    _logs.Add("report-digimon-exp-unwired");
                    _slotWarningLogged = true;
                }
                partner.Phase = PartnerPhase.SlotLevel;
                break;
            case PartnerPhase.SlotLevel:
                partner.Phase = PartnerPhase.NextSlot;
                break;
            case PartnerPhase.NextSlot:
                partner.SlotIndex++;
                partner.Phase = partner.SlotIndex < 3 ? PartnerPhase.SlotExp : PartnerPhase.PartnerLevel;
                break;
            case PartnerPhase.PartnerLevel:
                if (partner.Source.LevelAfter > partner.Source.LevelBefore)
                {
                    partner.LevelBlink = true;
                    StartMessage(7, partner.Source.PartnerId, partner.Source.LevelAfter);
                    partner.Phase = PartnerPhase.LevelMessage;
                }
                else partner.Phase = PartnerPhase.NewDigimon;
                break;
            case PartnerPhase.LevelMessage:
                if (_message?.Finished == true)
                {
                    _message = null;
                    partner.Phase = PartnerPhase.NewDigimon;
                }
                break;
            case PartnerPhase.NewDigimon:
                partner.Phase = PartnerPhase.WaitBlinks;
                break;
            case PartnerPhase.WaitBlinks:
                if (!partner.LevelBlink || partner.LevelBlinkFrame >= 10)
                {
                    partner.Selected = false;
                    partner.Phase = PartnerPhase.Idle;
                }
                break;
        }
    }

    private void RollExp(PartnerState partner)
    {
        var target = Math.Min(999999, partner.Source.ExpBefore + partner.Source.ExpGained);
        if (++partner.RollTime >= 10)
        {
            partner.ShownExp = target;
            return;
        }

        var step = RollStep(target);
        partner.ShownExp += step;
        if (partner.ShownExp > step * 9) partner.ShownExp -= step * 9 + 1;
    }

    private void TickReport()
    {
        switch (_phase)
        {
            case ReportPhase.Initial:
                _phase = ReportPhase.ShowPanels;
                break;
            case ReportPhase.ShowPanels:
                foreach (var partner in _partners) partner.Phase = PartnerPhase.ShowStart;
                _phase = ReportPhase.WaitPanels;
                break;
            case ReportPhase.WaitPanels:
                if (_partners.All(partner => partner.Phase == PartnerPhase.Idle))
                {
                    _footer.Start(true, 10);
                    _phase = ReportPhase.FooterIn;
                }
                break;
            case ReportPhase.FooterIn:
                if (_footer.Update()) _phase = ReportPhase.SelectPartner;
                break;
            case ReportPhase.SelectPartner:
                SelectPartner();
                break;
            case ReportPhase.HeaderIn:
                if (_header.Update())
                {
                    StartMessage(2, _partners[_partnerIndex].Source.ExpGained);
                    _phase = ReportPhase.ExpMessage;
                }
                break;
            case ReportPhase.ExpMessage:
                if (_message?.Finished == true)
                {
                    _message = null;
                    var partner = _partners[_partnerIndex];
                    partner.ExpSequenceStarted = true;
                    partner.Phase = PartnerPhase.SlotExp;
                    _phase = ReportPhase.RaisePartner;
                }
                break;
            case ReportPhase.RaisePartner:
                if (_partners[_partnerIndex].Phase == PartnerPhase.Idle)
                {
                    _header.Start(false, 10);
                    _partnerIndex++;
                    _phase = ReportPhase.HeaderOut;
                }
                break;
            case ReportPhase.HeaderOut:
                if (_header.Update()) _phase = ReportPhase.SelectPartner;
                break;
            case ReportPhase.Money:
                if (_message is null)
                {
                    StartMessage(10, _input.Money);
                    break;
                }
                if (_message?.Finished == true)
                {
                    _message = null;
                    if (_input.Item == 0) _phase = ReportPhase.StartExit;
                    else _phase = ReportPhase.ItemStart;
                }
                break;
            case ReportPhase.ItemStart:
                StartMessage(11, _input.Item);
                _phase = ReportPhase.ItemMessage;
                break;
            case ReportPhase.ItemMessage:
                if (_message?.Finished == true)
                {
                    _message = null;
                    _phase = ReportPhase.BeginExit;
                }
                break;
            case ReportPhase.StartExit:
                _phase = ReportPhase.BeginExit;
                break;
            case ReportPhase.BeginExit:
                StartExit();
                break;
            case ReportPhase.ExitPanels:
                if (_footer.Update() && _partners.All(partner => partner.Phase == PartnerPhase.Hidden))
                    _phase = ReportPhase.ExitFade;
                break;
            case ReportPhase.ExitFade:
                if (_exit.Update())
                {
                    _done = true;
                    _phase = ReportPhase.Done;
                }
                break;
        }
    }

    private void SelectPartner()
    {
        while (_partnerIndex < _partners.Length && _partners[_partnerIndex].Source.ExpGained == 0) _partnerIndex++;
        if (_partnerIndex >= _partners.Length)
        {
            _phase = ReportPhase.Money;
            return;
        }

        _partners[_partnerIndex].Selected = true;
        _header.Start(true, 10);
        _phase = ReportPhase.HeaderIn;
    }

    private void StartExit()
    {
        _exit.Start(true, 30);
        _footer.Start(false, 10);
        foreach (var partner in _partners) partner.Phase = PartnerPhase.HideStart;
        _phase = ReportPhase.ExitPanels;
    }

    private void StartMessage(int entry, params int[] substitutions)
    {
        var count = _input.MessageTokenCounts is not null && _input.MessageTokenCounts.TryGetValue(entry, out var configured)
            ? configured
            : 1;
        _message = new MessageState(entry, substitutions, Math.Max(1, count));
    }

    private FightReportView BuildView()
    {
        var partnerViews = _partners.Select(BuildPartnerView).ToArray();
        FightReportMessage? message = null;
        if (_message is { } active)
        {
            message = new FightReportMessage(active.Entry, active.Substitutions, active.Revealed, active.TokenCount,
                active.Waiting && !active.Finished, (_time / 5) % 5);
        }
        return new FightReportView(_header.Level, _footer.Level, _bgScroll, partnerViews, message, _exit.Level, _done);
    }

    private static FightReportPartnerView BuildPartnerView(PartnerState partner)
    {
        var slots = Enumerable.Range(0, 3)
            .Select(index => new FightReportSlotView(
                index < partner.Source.SlotDigimonIds.Count ? partner.Source.SlotDigimonIds[index] : -1,
                index < partner.Source.SlotDigimonIds.Count,
                false,
                0))
            .ToArray();
        return new FightReportPartnerView(partner.Source.PartyIndex, partner.Source.PartnerId, partner.Source.FighterId,
            partner.Fade.Level, partner.Selected, partner.CursorFrame, partner.LevelBlinkFrame, partner.ShownExp,
            partner.AnimationFrame, slots);
    }

    private sealed class Fade
    {
        public int Level { get; private set; }
        private int Step { get; set; }
        private bool Active { get; set; }

        public void Start(bool fadeIn, int duration)
        {
            Level = fadeIn ? 0 : FullFade;
            Step = (fadeIn ? 1 : -2) * (FullFade / duration);
            Active = true;
        }

        public bool Update()
        {
            if (!Active) return true;
            Level += Step;
            if (Step > 0 && Level > FullFade)
            {
                Level = FullFade;
                Active = false;
                return true;
            }
            if (Step < 0 && Level < 0)
            {
                Level = 0;
                Active = false;
                return true;
            }
            return false;
        }
    }

    private sealed class PartnerState(FightReportPartner source)
    {
        public FightReportPartner Source { get; } = source;
        public Fade Fade { get; } = new();
        public PartnerPhase Phase { get; set; }
        public bool Selected { get; set; }
        public int CursorFrame { get; set; }
        public bool LevelBlink { get; set; }
        public int LevelBlinkFrame { get; set; }
        public int ShownExp { get; set; } = source.ExpBefore;
        public int AnimationFrame { get; set; }
        public int SlotIndex { get; set; }
        public bool ExpSequenceStarted { get; set; }
        public int RollTime { get; set; }
    }

    private sealed class MessageState(int entry, IReadOnlyList<int> substitutions, int tokenCount)
    {
        public int Entry { get; } = entry;
        public IReadOnlyList<int> Substitutions { get; } = substitutions;
        public int TokenCount { get; } = tokenCount;
        public int Revealed { get; set; }
        public int RevealTimer { get; set; }
        public bool Waiting { get; set; }
        public bool Finished { get; set; }
    }

    private enum ReportPhase { Initial, ShowPanels, WaitPanels, FooterIn, SelectPartner, HeaderIn, ExpMessage, RaisePartner, HeaderOut, Money, ItemStart, ItemMessage, StartExit, BeginExit, ExitPanels, ExitFade, Done }
    private enum PartnerPhase { Idle, ShowStart, ShowFade, HideStart, HideFade, Hidden, SlotExp, SlotLevel, NextSlot, PartnerLevel, LevelMessage, NewDigimon, WaitBlinks }
}
