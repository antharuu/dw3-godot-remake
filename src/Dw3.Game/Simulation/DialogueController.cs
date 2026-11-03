using Dw3Production.Conversion;

namespace Dw3Production.Simulation;

/// <summary>
/// Phase of the dialogue state machine. Every phase except <see cref="Idle"/> suspends field
/// movement, warp offers, climbing and dropping.
/// </summary>
public enum DialogueState
{
    /// <summary>No conversation. Cross on an actor can start one.</summary>
    Idle,

    /// <summary>A conversation was started and the box opens on the next tick, which also logs the opening.</summary>
    Opening,

    /// <summary>A page is shown and typed out. Cross reveals the rest of the page, then advances or closes.</summary>
    Open,

    /// <summary>One-tick step to the next page, which restarts the typewriter.</summary>
    Advance,

    /// <summary>One-tick step that closes the box and reports an unapplied effect once.</summary>
    Closing,

    /// <summary>Waits until Cross is released, so the press that closed the box cannot reopen it.</summary>
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

    /// <summary>The typewriter delay in simulation ticks: the typewriter reveals
    /// one token every <see cref="TypeDelay"/>+1 simulation ticks (a Cross press reveals the page).</summary>
    public const int TypeDelay = 6;

    /// <summary>Gets a value indicating whether a conversation holds the player, from the start until Cross is released after closing.</summary>
    public bool IsActive => _state != DialogueState.Idle;

    /// <summary>Gets the current phase of the state machine.</summary>
    public DialogueState State => _state;

    /// <summary>Gets the actor of the current or last conversation, null before the first one.</summary>
    public DialogueActor? Actor => _actor;

    /// <summary>Gets the talk entry of the current or last conversation, null before the first one.</summary>
    public DialogueTalk? Talk => _talk;

    /// <summary>Gets the zero-based index of the page shown.</summary>
    public int PageIndex => _pageIndex;

    /// <summary>Gets the number of pages of the conversation, at least 1 once started.</summary>
    public int PageCount => _pages.Count;

    /// <summary>Gets the tokens of the page shown, empty before the first conversation.</summary>
    public IReadOnlyList<TextToken> CurrentPage => _pageIndex >= 0 && _pageIndex < _pages.Count ? _pages[_pageIndex] : [];

    /// <summary>Gets the zone name (<c>Snnn</c>) where the conversation was started, used in the log lines.</summary>
    public string? ZoneId => _zoneId;

    /// <summary>Gets the speaker-name tokens between the leading 02 07 and the closing 02 07 (empty when
    /// the talk has no name). A lone 02 09 (control 9) is the player's own name.</summary>
    public IReadOnlyList<TextToken> SpeakerName { get; private set; } = [];

    /// <summary>Gets a value indicating whether the speaker is the player (control 9 in the name field), whose name the renderer substitutes.</summary>
    public bool SpeakerIsPlayer { get; private set; }

    /// <summary>Gets the name-stripped full talk text (before paging), used to size the box from the
    /// whole text as the original box does, so the box keeps one size across pages.</summary>
    public IReadOnlyList<TextToken> FullText { get; private set; } = [];

    /// <summary>Gets how many tokens of the current page the typewriter has revealed so far.</summary>
    public int RevealCount => _revealCount;

    /// <summary>Gets a value indicating whether the current page is fully typed out.</summary>
    public bool RevealComplete => _pageIndex >= 0 && _pageIndex < _pages.Count && _revealCount >= _pages[_pageIndex].Count;

    /// <summary>Gets a value indicating whether the box waits for a Cross advance (a fully-typed non-final page), which shows the "next" arrow.</summary>
    public bool WaitingForAdvance => RevealComplete && _pageIndex + 1 < _pages.Count;

    /// <summary>Gets a value indicating whether the current (or just closed) conversation signalled its effect. Reset by <see cref="Start"/>.</summary>
    public bool EffectReported => _effectReported;

    /// <summary>Begins a conversation with <paramref name="actor"/>'s selected talk. The entry's
    /// speaker name is stripped, the remaining text is split into pages at each page break
    /// (control 3, see <see cref="SplitPages"/>), and the first page is typed out when the machine
    /// reaches Open.</summary>
    /// <param name="zoneId">Zone name where the conversation happens, for the log lines.</param>
    /// <param name="actor">Actor spoken to.</param>
    /// <param name="talk">Talk entry selected for the actor.</param>
    /// <param name="entry">Tokens of the talk's text entry, speaker name included.</param>
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

    /// <summary>Separates the speaker name from the talk text. The name sits between the leading 02 07 (control 7) and the
    /// next 02 07; a lone 02 09 (control 9) between them stands for the player's own name. The
    /// talk text is everything after the closing 02 07.</summary>
    /// <param name="entry">Tokens of a talk text entry.</param>
    /// <returns>The name tokens, whether they stand for the player, and the text. Without two control 7 tokens the name is empty and the text is the whole entry.</returns>
    private static (IReadOnlyList<TextToken> Name, bool IsPlayer, IReadOnlyList<TextToken> Text) SplitSpeaker(IReadOnlyList<TextToken> entry)
    {
        var first = -1;
        var second = -1;
        for (var index = 0; index < entry.Count; index++)
        {
            if (entry[index].IsControl && entry[index].Command == 7)
            {
                if (first < 0)
                {
                    first = index;
                }
                else
                {
                    second = index;
                    break;
                }
            }
        }

        if (first < 0 || second < 0)
        {
            return ([], false, entry);
        }

        var name = entry.Skip(first + 1).Take(second - first - 1).ToList();
        var isPlayer = name.Count == 1 && name[0].IsControl && name[0].Command == 9;
        return (name, isPlayer, entry.Skip(second + 1).ToList());
    }

    /// <summary>Advances the conversation by one simulation tick. Consumes the Cross press every tick, so a press made during the dialogue never reaches the field.</summary>
    /// <param name="input">Input sampled for the tick.</param>
    /// <param name="info">Sink for the open, page and close log lines.</param>
    /// <param name="warn">Keyed warning sink, called once per conversation when a talk of kind <c>effect</c> closes without its effect.</param>
    /// <exception cref="InvalidOperationException">The machine is ticked while <see cref="DialogueState.Idle"/>.</exception>
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
                if (!pressed)
                {
                    break;
                }

                if (!RevealComplete)
                {
                    _revealCount = _pages[_pageIndex].Count; // first Cross press reveals the whole page
                }
                else if (_pageIndex + 1 < _pages.Count)
                {
                    _state = DialogueState.Advance;
                }
                else
                {
                    _state = DialogueState.Closing;
                }

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
                    warn(
                        $"dialogue-effect-not-applied:{_zoneId}:{_actor!.Id}",
                        $"dialogue-effect-not-applied zone={_zoneId} actor={_actor.Id} families=[{string.Join(",", _talk.Families)}]");
                }

                info($"dialogue close zone={_zoneId} actor={_actor!.Id} entry={_talk!.Entry}");
                _state = DialogueState.Rearm;
                break;

            case DialogueState.Rearm:
                if (!input.ActionHeld)
                {
                    _state = DialogueState.Idle;
                }

                break;

            default:
                throw new InvalidOperationException($"Unhandled {nameof(DialogueState)}: {_state}");
        }
    }

    /// <summary>Types the current page one token at a time at the original cadence: the reveal
    /// counter advances by one token every <see cref="TypeDelay"/>+1 ticks, matching the original
    /// rhythm of about six frames per glyph.</summary>
    private void RevealTick()
    {
        var page = _pages[_pageIndex];
        if (_revealCount >= page.Count)
        {
            return;
        }

        if (++_revealTimer > TypeDelay)
        {
            _revealTimer = 0;
            _revealCount++;
        }
    }

    /// <summary>Control 3 (page break) is the only page boundary token.
    /// Control 2 (wait for a button) and control 6 (pause) are within-page pauses, never a new page,
    /// so a wait immediately followed by a page break must not produce a blank page.</summary>
    /// <param name="token">Token to test.</param>
    /// <returns><see langword="true"/> for control 3 only.</returns>
    public static bool IsPageBreak(TextToken token) => token.IsControl && token.Command == 3;

    /// <summary>The optional controls the dialogue runtime does not reconstruct (5 buffer
    /// insert, 8 player name). They carry no text of their own and are signalled, never rendered.</summary>
    /// <param name="token">Token to test.</param>
    /// <returns><see langword="true"/> for controls 5 and 8.</returns>
    public static bool IsUnsupported(TextToken token) => token.IsControl && token.Command is 5 or 8;

    /// <summary>Splits a token stream into pages at each page break (control 3). Wait (2) and pause
    /// (6) controls stay inside their page (they are skipped by the renderer), so a trailing page
    /// break produces no page of its own and the last non-empty page's Cross closes the conversation
    /// instead of showing a blank page.</summary>
    /// <param name="entry">Talk text without the speaker name.</param>
    /// <returns>At least one page. Only an entirely empty entry yields a single empty page.</returns>
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
        while (pages.Count > 1 && pages[^1].Count == 0)
        {
            pages.RemoveAt(pages.Count - 1);
        }

        return pages;
    }
}
