using Dw3Production.Diagnostics;
using Dw3Production.Diagnostics.Field;
using Dw3Production.Logging;
using Godot;

namespace Dw3Production;

/// <summary>Contains GameHost's Godot lifecycle and diagnostic-host forwarding methods.</summary>
public partial class GameHost
{
    /// <inheritdoc/>
    void IDiagnosticProbeHost.RunInputProbeAndQuit()
    {
        InputMappingProbe.Run();
        GetTree().Quit(0);
    }

    /// <inheritdoc/>
    void IDiagnosticProbeHost.ScheduleProbe(string methodName, bool ownsInput)
    {
        _testDriverActive = ownsInput;
        var run = ResolveProbe(methodName);

        // A block body keeps the callable an Action: an expression body would return the Task, which Godot cannot convert to a Variant.
        Callable.From(() => { _ = run(); }).CallDeferred();
    }

    /// <summary>
    /// Forwards interactive Godot input to the input adapter. Scripted probes own their injected
    /// input and suppress engine-delivered events to keep their deterministic route intact.
    /// </summary>
    /// <param name="event">Engine input event to forward.</param>
    public override void _Input(InputEvent @event)
    {
        if (_testDriverActive)
        {
            return;
        }

        _input.Handle(@event);
    }

    /// <summary>Synchronizes input focus and clears stale held actions on focus loss.</summary>
    /// <param name="what">Godot notification identifier.</param>
    public override void _Notification(int what)
    {
        if (what == NotificationWMWindowFocusOut)
        {
            _input.SetFocused(false);
            QuietLog.Info("input focus=out cleared=true");
        }
        else if (what == NotificationWMWindowFocusIn)
        {
            _input.SetFocused(true);
            QuietLog.Info("input focus=in awaiting-new-events=true");
        }
    }

    /// <summary>Disposes data-pack readers opened for this session before Godot removes the host node.</summary>
    public override void _ExitTree()
    {
        _battleLoader?.Dispose();
        _battleLoader = null;
        _fieldLoader?.Dispose();
        _fieldLoader = null;
        _animationLoader?.Dispose();
        _animationLoader = null;

        // Dispose Godot RefCounted resources that the host created and that the C# GC may
        // not finalize before the engine tears down native objects, which would otherwise
        // produce "Leaked unsafe reference" errors (exit 134) during probe shutdown.
        _dialogueRenderer?.Dispose();
        _dialogueRenderer = null;
        _panelTexture?.Dispose();
        _panelTexture = null;
        GodotObjectDisposal.DisposeValues(_frameTextures);
        _frameTextures = null;
        _battleNoticeStyleBox?.Dispose();
        _battleNoticeStyleBox = null;
    }

    /// <summary>Advances fixed simulation ticks and presents one final state for the rendered frame.</summary>
    /// <param name="delta">Elapsed rendered-frame time.</param>
    public override void _Process(double delta) => _frameCoordinator.Advance(this, delta);
}
