using Dw3Production.Logging;
using static Dw3Production.Diagnostics.Field.ProbeRequirement;

namespace Dw3Production.Diagnostics.Field;

/// <summary>
/// M2 real-time traversal video: drives <see cref="CityLoopRoute"/> from the S200 spawn, records a
/// frame on every 60 Hz simulation tick and encodes them at 60 fps, so playback runs at gameplay
/// speed. Every frame comes from the shared simulation step under native held input.
/// </summary>
internal sealed class M2VideoProbe : FieldProbe
{
    /// <summary>Video frame rate, equal to the simulation rate.</summary>
    private const int RealTimeFrameRate = 60;

    /// <summary>Initializes a new instance of the <see cref="M2VideoProbe"/> class.</summary>
    /// <param name="host">Running game.</param>
    public M2VideoProbe(IFieldProbeHost host)
        : base(host)
    {
    }

    /// <inheritdoc/>
    protected override string FailureCode => "M2_VIDEO_PROBE";

    /// <inheritdoc/>
    protected override async Task ExecuteAsync()
    {
        Host.PrepareCaptureDirectory();
        var videoRoot = Host.PrepareVideoDirectory();
        var frameRoot = Path.Combine(videoRoot, "m2-video-frames");
        ProbeVideoEncoder.ResetFrameDirectory(frameRoot);
        Require(CityLoopRoute.StandsAtSpawn(Host), "M2 video must begin at the source S200 spawn without repositioning");

        var frames = new FrameSequenceRecorder(Host, frameRoot, everyTick: true);
        await CityLoopRoute.DriveAsync(Host, frames);

        Require(Host.Warp.CommitCount == CityLoopRoute.Commits, $"M2 video committed {Host.Warp.CommitCount} times instead of four");
        Require(CityLoopRoute.EndedAtFinalArrival(Host), "M2 video did not return to S200 via S235 slot1");
        Require(!Host.Input.F3On, "M2 video enabled F3");
        var videoPath = Path.Combine(videoRoot, "m2-traversal.mp4");
        ProbeVideoEncoder.Encode(frameRoot, videoPath, RealTimeFrameRate);
        Require(File.Exists(videoPath) && new FileInfo(videoPath).Length > 0, "ffmpeg produced no m2-traversal.mp4");
        QuietLog.Info($"m2 video PASS frames={frames.NextFrame} commits={Host.Warp.CommitCount} route=S200->S210->S200->S235->S200 video={videoPath}");
        ProbeMemory.CollectBeforeQuit();
    }
}
