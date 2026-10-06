using System.Collections.Concurrent;

namespace SiegeFX.Runtime.Capture;

/// <summary>SC-RECORD — the in-game video recorder as the host sees it. The
/// Windows build records through Windows.Graphics.Capture (<c>WgcRecorder</c>,
/// compiled for the Windows target only); every other build gets
/// <see cref="NullRecorder"/>, and the recording controls (the Record Video
/// hotkey and the Videos Folder row) are left out of that build.</summary>
public interface IVideoRecorder
{
    bool IsRecording { get; }
    TimeSpan Elapsed { get; }

    /// <summary>Status lines for the game's message strip. Produced on
    /// worker threads; the host drains once per frame on the render
    /// thread.</summary>
    ConcurrentQueue<string> StatusLines { get; }

    bool Start(nint hwnd, string outputDir, string fileStem);
    void Stop();
}

/// <summary>The recorder of a build without one: never records.</summary>
public sealed class NullRecorder : IVideoRecorder
{
    public bool IsRecording => false;
    public TimeSpan Elapsed => TimeSpan.Zero;
    public ConcurrentQueue<string> StatusLines { get; } = new();

    public bool Start(nint hwnd, string outputDir, string fileStem)
    {
        StatusLines.Enqueue("Video recording is not available in this build.");
        return false;
    }

    public void Stop() { }
}

public static class VideoRecorder
{
    /// <summary>The platform's recorder: WGC on the Windows build, else none.</summary>
    public static IVideoRecorder Create()
#if WINDOWS
        => new WgcRecorder();
#else
        => new NullRecorder();
#endif
}
