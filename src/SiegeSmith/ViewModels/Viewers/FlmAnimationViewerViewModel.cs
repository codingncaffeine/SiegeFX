using System;
using System.Collections.Generic;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SiegeFX.Core.Assets;
using SiegeSmith.Mvvm;

namespace SiegeSmith.ViewModels.Viewers;

/// <summary>Viewer for a .flm animated cursor "film strip": a row of frame thumbnails, a large
/// current-frame view, and a play/pause loop. Frames arrive from <see cref="FlmAnimation.LoadFrames"/>
/// as bottom-up RGBA, so each is flipped vertically and swizzled to BGRA for display. Disposable —
/// the loop timer stops when the preview is swapped out.</summary>
public sealed class FlmAnimationViewerViewModel : ObservableObject, IDisposable
{
    public string Name { get; }
    public string Info { get; }
    public IReadOnlyList<BitmapSource> Frames { get; }

    private int _index;
    public int Index
    {
        get => _index;
        set { if (SetProperty(ref _index, value)) OnPropertyChanged(nameof(CurrentFrame)); }
    }

    public BitmapSource? CurrentFrame =>
        Frames.Count > 0 ? Frames[Math.Clamp(_index, 0, Frames.Count - 1)] : null;

    private readonly DispatcherTimer _timer;
    private bool _playing;
    public string PlayLabel => _playing ? "❚❚ Pause" : "▶ Play";

    public RelayCommand PlayCommand { get; }

    public FlmAnimationViewerViewModel(string name, byte[][] frames)
    {
        Name = name;
        var list = new List<BitmapSource>(frames.Length);
        foreach (var f in frames) list.Add(FrameToBitmap(f, FlmAnimation.FrameSize));
        Frames = list;
        Info = $"{frames.Length} frame(s) · {FlmAnimation.FrameSize}×{FlmAnimation.FrameSize}";

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _timer.Tick += (_, _) => { if (Frames.Count > 0) Index = (Index + 1) % Frames.Count; };
        PlayCommand = new RelayCommand(_ => TogglePlay(), _ => Frames.Count > 1);
    }

    private void TogglePlay()
    {
        _playing = !_playing;
        if (_playing) _timer.Start(); else _timer.Stop();
        OnPropertyChanged(nameof(PlayLabel));
    }

    /// <summary>Bottom-up RGBA frame → top-down BGRA32 BitmapSource.</summary>
    private static BitmapSource FrameToBitmap(byte[] rgbaBottomUp, int size)
    {
        int stride = size * 4;
        var bgra = new byte[stride * size];
        for (int y = 0; y < size; y++)
        {
            int src = (size - 1 - y) * stride;
            int dst = y * stride;
            for (int x = 0; x < size; x++)
            {
                int s = src + x * 4, d = dst + x * 4;
                bgra[d] = rgbaBottomUp[s + 2];     // B
                bgra[d + 1] = rgbaBottomUp[s + 1]; // G
                bgra[d + 2] = rgbaBottomUp[s];     // R
                bgra[d + 3] = rgbaBottomUp[s + 3]; // A
            }
        }
        var bmp = BitmapSource.Create(size, size, 96, 96, PixelFormats.Bgra32, null, bgra, stride);
        bmp.Freeze();
        return bmp;
    }

    public void Dispose() => _timer.Stop();
}
