using Dw3Production.Diagnostics.Field;
using Dw3Production.Logging;
using Dw3Production.Simulation;
using Godot;

namespace Dw3Production.Diagnostics.Battle;

/// <summary>
/// Drives an open fight report through rendered frames, the way a player reads it: every step advances the
/// production battle tick once and waits for that frame to be drawn, every page is captured while its arrow
/// waits, and each page closes only through one injected Cross edge. Confirmations are counted from the view
/// itself (a waiting arrow that stops waiting), so an auto-closed page or a held key read as repeated presses
/// is detected rather than assumed away.
/// </summary>
internal sealed class BattleReportPageWalker
{
    /// <summary>Upper bound of rendered frames spent waiting for the next arrow or the report end.</summary>
    private const int ArrowWaitFrames = 1200;

    /// <summary>Rendered frames the first page must stay open without any input.</summary>
    private const int IdleHoldFrames = 600;

    /// <summary>Rendered frames Cross stays held on the held-input page.</summary>
    private const int HeldFrames = 45;

    private readonly IBattleProbeHost _host;
    private readonly string _captureRoot;
    private readonly Action<int, bool>? _observe;
    private readonly List<int> _entries = [];
    private bool _wasWaiting;
    private int _frame;
    private int _presses;
    private int _confirmations;

    /// <summary>Initializes a new instance of the <see cref="BattleReportPageWalker"/> class.</summary>
    /// <param name="host">Live battle host whose report owns input.</param>
    /// <param name="captureRoot">Directory receiving one PNG per report page.</param>
    /// <param name="observe">Optional check run with each page's entry when it waits (false) and once it was confirmed (true).</param>
    public BattleReportPageWalker(IBattleProbeHost host, string captureRoot, Action<int, bool>? observe = null)
    {
        _host = host;
        _captureRoot = captureRoot;
        _observe = observe;
    }

    /// <summary>Gets the message entries confirmed, in reading order.</summary>
    public IReadOnlyList<int> Entries => _entries;

    /// <summary>Gets the number of rendered report frames advanced so far.</summary>
    public int Frames => _frame;

    /// <summary>Reads every report page until the report closes, holding the first page and holding Cross on the second.</summary>
    /// <returns>A task completed once the report has handed control back.</returns>
    /// <exception cref="InvalidOperationException">A page auto-closed, a press was not consumed exactly once, or the report never ended.</exception>
    public async Task WalkAsync()
    {
        var page = 0;
        while (await WaitForArrowAsync() is { } message)
        {
            ProbeRequirement.Require(
                message.TokenCount > 1 && message.TokensRevealed == message.TokenCount,
                $"report entry {message.Entry} waits with {message.TokensRevealed}/{message.TokenCount} typed glyphs");
            var name = $"report-page-{page:00}-entry-{message.Entry:00}.png";
            await BattleProbeCapture.CaptureRawAsync(_host, _captureRoot, name);
            QuietLog.Info($"battle-report checkpoint=page index={page} frame={_frame} entry={message.Entry} values=[{string.Join(",", message.Substitutions)}] tokens={message.TokensRevealed}/{message.TokenCount} capture={name}");
            _observe?.Invoke(message.Entry, false);
            if (page == 0)
            {
                await HoldWithoutInputAsync();
            }

            await ConfirmAsync(message.Entry, page == 1 ? HeldFrames : 1);
            _observe?.Invoke(message.Entry, true);
            page++;
        }

        ProbeRequirement.Require(
            _confirmations == _presses,
            $"report confirmations {_confirmations} differ from injected Cross presses {_presses}");
        QuietLog.Info($"battle-report checkpoint=report-closed frame={_frame} pages={page} entries=[{string.Join(",", _entries)}] presses={_presses} confirmations={_confirmations}");
    }

    /// <summary>Advances one production tick and waits for its drawn frame, counting arrow confirmations.</summary>
    /// <returns>A task completed after the frame was drawn.</returns>
    private async Task StepAsync()
    {
        _host.TickTestSimulation();
        await _host.NextFrameDrawn();
        _frame++;
        var waiting = _host.FightReportController?.View.Message?.ArrowVisible == true;
        if (_wasWaiting && !waiting)
        {
            _confirmations++;
        }

        _wasWaiting = waiting;
    }

    /// <summary>Steps frames without input until a page waits on Cross or the report hands control back.</summary>
    /// <returns>The waiting page, or <see langword="null"/> when the report ended.</returns>
    /// <exception cref="InvalidOperationException">Neither happened within the bound.</exception>
    private async Task<FightReportMessage?> WaitForArrowAsync()
    {
        for (var guard = 0; guard < ArrowWaitFrames; guard++)
        {
            if (_host.FightReportController is not { } report)
            {
                return null;
            }

            if (report.View.Message is { ArrowVisible: true } message)
            {
                return message;
            }

            await StepAsync();
        }

        throw new InvalidOperationException($"report neither waited on Cross nor ended within {ArrowWaitFrames} frames frame={_frame}");
    }

    /// <summary>Proves the waiting page stays open and unconfirmed without input.</summary>
    /// <returns>A task completed after the hold.</returns>
    private async Task HoldWithoutInputAsync()
    {
        var confirmations = _confirmations;
        for (var i = 0; i < IdleHoldFrames; i++)
        {
            await StepAsync();
        }

        ProbeRequirement.Require(
            _host.FightReportController?.View.Message?.ArrowVisible == true && _confirmations == confirmations,
            $"report page closed without Cross within {IdleHoldFrames} frames");
        QuietLog.Info($"battle-report checkpoint=idle-hold frames={IdleHoldFrames} frame={_frame} still-waiting=true");
    }

    /// <summary>Presses Cross, keeps it held for a number of frames, then releases it, requiring exactly one confirmation.</summary>
    /// <param name="entry">Message entry of the waiting page.</param>
    /// <param name="heldFrames">Rendered frames during which Cross stays down.</param>
    /// <returns>A task completed after the release frame.</returns>
    private async Task ConfirmAsync(int entry, int heldFrames)
    {
        var confirmations = _confirmations;
        SetCross(true);
        _presses++;
        for (var i = 0; i < heldFrames; i++)
        {
            await StepAsync();
        }

        SetCross(false);
        await StepAsync();
        ProbeRequirement.Require(
            _confirmations == confirmations + 1,
            $"Cross on entry {entry} held {heldFrames} frames confirmed {_confirmations - confirmations} pages");
        _entries.Add(entry);
        QuietLog.Info($"battle-report checkpoint=cross-consumed entry={entry} held-frames={heldFrames} frame={_frame} next={_host.FightReportController?.View.Message?.Entry.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none"}");
    }

    /// <summary>Injects one native Cross key state without advancing simulation.</summary>
    /// <param name="pressed">Whether the event is a press or release.</param>
    private void SetCross(bool pressed) => _host.Input.Handle(new InputEventKey { Keycode = Key.Enter, Pressed = pressed });
}
