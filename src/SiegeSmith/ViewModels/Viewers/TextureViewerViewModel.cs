using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SiegeFX.Core.Assets;
using SiegeSmith.Mvvm;
using SiegeSmith.Services;

namespace SiegeSmith.ViewModels.Viewers;

/// <summary>Renders a Dungeon Siege .raw texture. RAW is BGRA 8:8:8:8 with an optional mip
/// chain, which maps directly onto WPF's <see cref="PixelFormats.Bgra32"/> — so each surface
/// becomes a frozen <see cref="BitmapSource"/> with no colour swizzle. Prev/Next step through
/// the mip surfaces; Export writes the current surface to PNG.</summary>
public sealed class TextureViewerViewModel : ObservableObject
{
    private readonly RawImage _img;

    public string Name { get; }
    public bool HasMips => _img.SurfaceCount > 1;
    public string Info => $"{_img.Width} × {_img.Height}   ·   BGRA8888   ·   {_img.SurfaceCount} surface(s)";
    public string SurfaceLabel => $"surface {_surface}  ·  {_img.GetSurfaceWidth(_surface)} × {_img.GetSurfaceHeight(_surface)}";

    private int _surface;
    public int Surface
    {
        get => _surface;
        set
        {
            var clamped = Math.Clamp(value, 0, _img.SurfaceCount - 1);
            if (SetProperty(ref _surface, clamped))
            {
                UpdateImage();
                OnPropertyChanged(nameof(SurfaceLabel));
                PrevSurfaceCommand.RaiseCanExecuteChanged();
                NextSurfaceCommand.RaiseCanExecuteChanged();
            }
        }
    }

    private BitmapSource? _bitmap;
    public BitmapSource? Bitmap { get => _bitmap; private set => SetProperty(ref _bitmap, value); }

    public RelayCommand PrevSurfaceCommand { get; }
    public RelayCommand NextSurfaceCommand { get; }
    public RelayCommand ExportPngCommand { get; }

    public TextureViewerViewModel(string name, RawImage img)
    {
        Name = name;
        _img = img;
        PrevSurfaceCommand = new RelayCommand(_ => Surface--, _ => _surface > 0);
        NextSurfaceCommand = new RelayCommand(_ => Surface++, _ => _surface < _img.SurfaceCount - 1);
        ExportPngCommand = new RelayCommand(_ => ExportPng());
        _surface = 0;
        UpdateImage();
    }

    private void UpdateImage()
    {
        int w = _img.GetSurfaceWidth(_surface);
        int h = _img.GetSurfaceHeight(_surface);
        int stride = w * 4;
        var slice = new byte[stride * h];
        Buffer.BlockCopy(_img.Pixels, _img.GetSurfaceOffset(_surface), slice, 0, slice.Length);
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, slice, stride);
        bmp.Freeze();
        Bitmap = bmp;
    }

    private void ExportPng()
    {
        if (Bitmap is null) return;
        var dest = DialogService.SaveFileAs(Path.GetFileNameWithoutExtension(Name) + ".png");
        if (dest is null) return;
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(Bitmap));
        using var fs = File.Create(dest);
        encoder.Save(fs);
    }
}
