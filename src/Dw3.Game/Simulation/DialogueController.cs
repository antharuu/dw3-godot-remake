using Dw3Production.Conversion;

namespace Dw3Production.Simulation;

public enum DialogueState
{
    Idle,
    Opening,
    Open,
    Advance,
    Closing,
    Rearm,
}

/// <summary>
/// The player dialogue state machine: Idle -> Opening -> Open (a page is shown) -> Advance
/// (next page) -> Closing -> Rearm -> Idle. It is a modal simulation state, not Godot input:
/// while it is active, locomotion, warp offers and climb/drop are all suspended. The same
/// Cross press that closes a conversation cannot immediately reopen it: after Closing the
/// machine waits in Rearm until the action button is released (the warp's Rearm pattern).
/// Effects are never applied: a talk whose kind is "effect" is signalled once per
/// conversation and nothing (flag, item, event, progress) changes.
/// </summary>
public sealed class DialogueController
{
    private DialogueState _state = DialogueState.Idle;
    private DialogueActor? _actor;
    private DialogueTalk? _talk;
    private IReadOnlyList<IReadOnlyList<TextToken>> _pages = [];
    private int _pageIndex;
    private bool _effectReported;
    private string? _zoneId;
    private int _revealCount;
    private int _revealTimer;

    /// <summary>The typewriter type delay (in simulation ticks): the typewriter reveals
    /// one token every <see cref="TypeDelay"/>+1 simulation ticks (a Cross press reveals the page).</summary>
    public const int TypeDelay = 6;

    public bool IsActive => _state != DialogueState.Idle;
    public DialogueState State => _state;
    public DialogueActor? Actor => _actor;
    public DialogueTalk? Talk => _talk;
    public int PageIndex => _pageIndex;
    public int PageCount => _pages.Count;
    public IReadOnlyList<TextToken> CurrentPage => _pageIndex >= 0 && _pageIndex < _pages.Count ? _pages[_pageIndex] : [];
    public string? ZoneId => _zoneId;
    /// <summary>The speaker-name tokens between the leading 02 07 and the closing 02 07 (empty when
    /// the talk has no name). A lone 02 09 (control 9) is the player's own name.</summary>
    public IReadOnlyList<TextToken> SpeakerName { get; private set; } = [];
    /// <summary>True when the speaker is the player (control 9 in the name field).</summary>
    public bool SpeakerIsPlayer { get; private set; }
    /// <summary>The name-stripped full talk text (before paging), used to size the box like
    /// createTalkBox's measure of the whole text.</summary>
    public IReadOnlyList<TextToken> FullText { get; private set; } = [];
    /// <summary>How many tokens of the current page the typewriter has revealed so far.</summary>
    public int RevealCount => _revealCount;
    /// <summary>True when the current page is fully typed out.</summary>
    public bool RevealComplete => _pageIndex >= 0 && _pageIndex < _pages.Count && _revealCount >= _pages[_pageIndex].Count;
    /// <summary>True when the box waits for a Cross advance (a fully-typed non-final page).</summary>
    public bool WaitingForAdvance => RevealComplete && _pageIndex + 1 < _pages.Count;
    /// <summary>True once the current (or just closed) conversation signalled its effect.</summary>
    public bool EffectReported => _effectReported;

    /// <summary>Begins a conversation with <paramref name="actor"/>'s selected talk. The entry's
    /// speaker name is stripped, the remaining text is split into pages at the advance-point
    /// controls (2 wait, 3 page-break, 6 pause), and the first page is typed out when the machine
    /// reaches Open.</summary>
    public void Start(string zoneId, DialogueActor actor, DialogueTalk talk, IReadOnlyList<TextToken> entry)
    {
        _zoneId = zoneId;
        _actor = actor;
        _talk = talk;
        var (name, isPlayer, text) = SplitSpeaker(entry);
        SpeakerName = name;
        SpeakerIsPlayer = isPlayer;
        FullText = text;
        _pages = SplitPages(text);
        _pageIndex = 0;
        _effectReported = false;
        _revealCount = 0;
        _revealTimer = 0;
        _state = DialogueState.Opening;
    }

    /// <summary>createTalkBox reads the speaker name between the leading 02 07 (control 7) and the
    /// next 02 07; a lone 02 09 (control 9) between them is the player's own name (GAME.name). The
    /// talk text is everything after the closing 02 07.</summary>
    private static (IReadOnlyList<TextToken> Name, bool IsPlayer, IReadOnlyList<TextToken> Text) SplitSpeaker(IReadOnlyList<TextToken> entry)
    {
        var first = -1;
        var second = -1;
        for (var index = 0; index < entry.Count; index++)
        {
            if (entry[index].IsControl && entry[index].Command == 7)
            {
                if (first < 0) first = index;
                else { second = index; break; }
            }
        }
        if (first < 0 || second < 0) return ([], false, entry);
        var name = entry.Skip(first + 1).Take(second - first - 1).ToList();
        var isPlayer = name.Count == 1 && name[0].IsControl && name[0].Command == 9;
        return (name, isPlayer, entry.Skip(second + 1).ToList());
    }

    public void Tick(InputState input, Action<string> info, Action<string, string> warn)
    {
        var pressed = input.ConsumeActionPressed();
        switch (_state)
        {
            case DialogueState.Opening:
                info($"dialogue open zone={_zoneId} actor={_actor!.Id} entry={_talk!.Entry} kind={_talk.Kind} pages={_pages.Count}");
                _revealCount = 0;
                _revealTimer = 0;
                _state = DialogueState.Open;
                break;

            case DialogueState.Open:
                RevealTick();
                if (!pressed) break;
                if (!RevealComplete)
                    _revealCount = _pages[_pageIndex].Count; // source showPage: reveal the whole page
                else if (_pageIndex + 1 < _pages.Count)
                    _state = DialogueState.Advance;
                else
                    _state = DialogueState.Closing;
                break;

            case DialogueState.Advance:
                _pageIndex++;
                info($"dialogue page zone={_zoneId} actor={_actor!.Id} page={_pageIndex + 1}/{_pages.Count}");
                _revealCount = 0;
                _revealTimer = 0;
                _state = DialogueState.Open;
                break;

            case DialogueState.Closing:
                if (!_effectReported && _talk!.Kind == "effect")
                {
                    _effectReported = true;
                    warn($"dialogue-effect-not-applied:{_zoneId}:{_actor!.Id}",
                        $"dialogue-effect-not-applied zone={_zoneId} actor={_actor.Id} families=[{string.Join(",", _talk.Families)}]");
                }
                info($"dialogue close zone={_zoneId} actor={_actor!.Id} entry={_talk!.Entry}");
                _state = DialogueState.Rearm;
                break;

            case DialogueState.Rearm:
                if (!input.ActionHeld) _state = DialogueState.Idle;
                break;

            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    /// <summary>Types the current page one token at a time, mirroring updateTextWindow's typeDelay
    /// cadence: the reveal counter advances by one token every <see cref="TypeDelay"/>+1 ticks, so
    /// the typewriter is cohérent with the source's ~6-frame-per-glyph rhythm.</summary>
    private void RevealTick()
    {
        var page = _pages[_pageIndex];
        if (_revealCount >= page.Count) return;
        if (++_revealTimer > TypeDelay)
        {
            _revealTimer = 0;
            _revealCount++;
        }
    }

    /// <summary>Control 3 (page break) is the only page boundary token.
    /// Control 2 (wait for a button) and control 6 (pause) are within-page pauses, never a new page,
    /// so a wait immediately followed by a page break must not produce a blank page.</summary>
    public static bool IsPageBreak(TextToken token) => token.IsControl && token.Command == 3;

    /// <summary>The optional controls the M4 runtime does not reconstruct honestly (5 buffer
    /// insert, 8 player name). They carry no text of their own and are signalled, never rendered.</summary>
    public static bool IsUnsupported(TextToken token) => token.IsControl && token.Command is 5 or 8;

    /// <summary>Splits a token stream into pages at each page break (control 3). Wait (2) and pause
    /// (6) controls stay inside their page (they are skipped by the renderer), so a trailing page
    /// break produces no page of its own and the last non-empty page's Cross closes the conversation
    /// instead of showing a blank page.</summary>
    public static IReadOnlyList<IReadOnlyList<TextToken>> SplitPages(IReadOnlyList<TextToken> entry)
    {
        var pages = new List<IReadOnlyList<TextToken>>();
        var current = new List<TextToken>();
        foreach (var token in entry)
        {
            if (IsPageBreak(token))
            {
                pages.Add(current);
                current = [];
            }
            else
            {
                current.Add(token);
            }
        }
        pages.Add(current);
        while (pages.Count > 1 && pages[^1].Count == 0) pages.RemoveAt(pages.Count - 1);
        return pages;
    }
}
