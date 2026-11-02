using System.Diagnostics;
using System.Globalization;

namespace Dw3Production.Diagnostics.Field;

/// <summary>Encodes a numbered PNG frame sequence into an H.264 video with the system <c>ffmpeg</c>.</summary>
internal static class ProbeVideoEncoder
{
    /// <summary>Frame rate of the acceptance route videos, which keep one frame every few ticks.</summary>
    public const int AcceptanceFrameRate = 12;

    /// <summary>Encodes <c>frame-0000.png</c>, <c>frame-0001.png</c>... into a yuv420p H.264 file.</summary>
    /// <param name="frameRoot">Directory holding the numbered frames.</param>
    /// <param name="videoPath">Output video path, overwritten if present.</param>
    /// <param name="framerate">Frames per second of the output.</param>
    /// <exception cref="InvalidOperationException">ffmpeg could not start or exited with a non-zero code.</exception>
    public static void Encode(string frameRoot, string videoPath, int framerate)
    {
        var startInfo = new ProcessStartInfo(LocateFfmpeg()) { UseShellExecute = false };
        startInfo.ArgumentList.Add("-y");
        startInfo.ArgumentList.Add("-framerate");
        startInfo.ArgumentList.Add(framerate.ToString(CultureInfo.InvariantCulture));
        startInfo.ArgumentList.Add("-i");
        startInfo.ArgumentList.Add(Path.Combine(frameRoot, "frame-%04d.png"));
        startInfo.ArgumentList.Add("-c:v");
        startInfo.ArgumentList.Add("libx264");
        startInfo.ArgumentList.Add("-pix_fmt");
        startInfo.ArgumentList.Add("yuv420p");
        startInfo.ArgumentList.Add(videoPath);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("ffmpeg did not start");
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"ffmpeg exit={process.ExitCode}");
        }
    }

    /// <summary>Deletes a frame directory left by an earlier run and creates it empty.</summary>
    /// <param name="frameRoot">Directory that receives the numbered frames.</param>
    public static void ResetFrameDirectory(string frameRoot)
    {
        if (Directory.Exists(frameRoot))
        {
            Directory.Delete(frameRoot, recursive: true);
        }

        Directory.CreateDirectory(frameRoot);
    }

    /// <summary>Resolves the ffmpeg executable from the directories of the <c>PATH</c> variable, in order.</summary>
    /// <returns>Absolute path of the first <c>ffmpeg</c> (or <c>ffmpeg.exe</c>) found.</returns>
    /// <exception cref="InvalidOperationException">No directory of <c>PATH</c> holds ffmpeg.</exception>
    private static string LocateFfmpeg()
    {
        var directories = (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries);
        return directories
            .SelectMany(directory => new[] { Path.Combine(directory, "ffmpeg"), Path.Combine(directory, "ffmpeg.exe") })
            .FirstOrDefault(File.Exists)
            ?? throw new InvalidOperationException("ffmpeg did not start: not found on PATH");
    }
}
