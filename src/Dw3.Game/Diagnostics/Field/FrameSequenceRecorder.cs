using System.Globalization;
using Godot;

namespace Dw3Production.Diagnostics.Field;

/// <summary>
/// Writes the numbered frames <c>frame-NNNN.png</c> of a scripted route, later encoded by
/// <see cref="ProbeVideoEncoder"/>. Every frame presents the current state with the live F3 setting.
/// </summary>
internal sealed class FrameSequenceRecorder
{
    /// <summary>Running game whose viewport is captured.</summary>
    private readonly IFieldProbeHost _host;

    /// <summary>Directory that receives the frames.</summary>
    private readonly string _frameRoot;

    /// <summary>When set, every simulation tick is recorded instead of one tick per sampling period.</summary>
    private readonly bool _everyTick;

    /// <summary>Initializes a new instance of the <see cref="FrameSequenceRecorder"/> class.</summary>
    /// <param name="host">Running game whose viewport is captured.</param>
    /// <param name="frameRoot">Directory that receives the frames.</param>
    /// <param name="everyTick">Record every tick, for real-time videos.</param>
    /// <param name="firstFrame">Number of the first frame written; earlier files with the same numbers are overwritten.</param>
    public FrameSequenceRecorder(IFieldProbeHost host, string frameRoot, bool everyTick = false, int firstFrame = 0)
    {
        _host = host;
        _frameRoot = frameRoot;
        _everyTick = everyTick;
        NextFrame = firstFrame;
    }

    /// <summary>Gets the number the next frame file will carry, which is also the count written from 0.</summary>
    public int NextFrame { get; private set; }

    /// <summary>Records the current state as the next frame.</summary>
    /// <returns>A task that completes once the PNG is written.</returns>
    /// <exception cref="InvalidOperationException">The PNG could not be written.</exception>
    public async Task RecordAsync()
    {
        _host.Renderer.Present(_host.State, _host.CurrentZone, _host.Input.F3On);
        await _host.NextFrameDrawn();
        var framePath = Path.Combine(_frameRoot, $"frame-{NextFrame.ToString("D4", CultureInfo.InvariantCulture)}.png");
        NextFrame++;

        // One image per recorded tick: dispose each at once so thousands of engine images are not left to the finalizer.
        using Image frame = _host.CaptureViewport();
        ProbeRequirement.Require(frame.SavePng(framePath) == Error.Ok, $"video frame write failed={framePath}");
    }

    /// <summary>Records the current state when <paramref name="tick"/> falls on the sampling period, or always in every-tick mode.</summary>
    /// <param name="tick">Zero-based tick inside the current phase.</param>
    /// <param name="period">Ticks between two recorded frames.</param>
    /// <returns>A task that completes once the frame, if any, is written.</returns>
    public async Task RecordIfDueAsync(int tick, int period)
    {
        if (_everyTick || tick % period == 0)
        {
            await RecordAsync();
        }
    }
}
