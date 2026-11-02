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
        Callable.From(() => _ = run()).CallDeferred();
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
    }

    /// <summary>Advances fixed simulation ticks and presents one final state for the rendered frame.</summary>
    /// <param name="delta">Elapsed rendered-frame time.</param>
    public override void _Process(double delta) => _frameCoordinator.Advance(this, delta);
}
