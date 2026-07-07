using System;
using System.IO;
using SiegeFX.Core.Assets;
using SiegeSmith.ViewModels.Viewers;

namespace SiegeSmith.Services;

/// <summary>Picks a format-aware viewer for a tank file: .raw → texture, otherwise a
/// text/binary heuristic chooses the text or hex fallback. Returns an <see cref="object"/>
/// so the shell can template it by concrete view-model type.</summary>
public static class ViewerFactory
{
    public static object Create(string name, byte[] bytes)
    {
        var ext = Path.GetExtension(name).ToLowerInvariant();

        if (ext == ".raw")
        {
            try { return new TextureViewerViewModel(name, RawImage.Load(bytes)); }
            catch { /* corrupt/unsupported RAW — fall through to hex */ }
        }

        if (ext == ".gas")
        {
            try { return new GasViewerViewModel(name, bytes); }
            catch (Exception ex) { return new TextViewerViewModel(name, bytes, "GAS parse error — " + ex.Message); }
        }

        if (ext == ".skrit")
            return new SkritViewerViewModel(name, bytes);

        if (ext == ".wav")
            return new AudioViewerViewModel(name, bytes, isWav: true);

        if (ext == ".mp3")
            return new AudioViewerViewModel(name, bytes, isWav: false);

        if (ext == ".asp")
        {
            try { return ModelInfoViewerViewModel.FromAsp(name, AspMesh.Load(bytes)); }
            catch { /* fall through to hex */ }
        }

        if (ext == ".sno")
        {
            try { return ModelInfoViewerViewModel.FromSno(name, SnoModel.Load(bytes)); }
            catch { /* fall through to hex */ }
        }

        if (ext == ".prs")
        {
            try { return new PrsAnimationViewerViewModel(name, PrsAnimation.Load(bytes)); }
            catch { /* fall through to hex */ }
        }

        if (ext == ".flm")
        {
            try
            {
                var frames = FlmAnimation.LoadFrames(bytes);
                if (frames.Length > 0) return new FlmAnimationViewerViewModel(name, frames);
            }
            catch { /* fall through to hex */ }
        }

        return LooksLikeText(bytes)
            ? new TextViewerViewModel(name, bytes)
            : new HexViewerViewModel(name, bytes);
    }

    /// <summary>Heuristic: a NUL byte means binary; otherwise the sample must be ≥90%
    /// printable/whitespace. DS1 gas/skrit/config are ASCII, while meshes/anims/fonts carry
    /// NULs, so this reliably separates the two.</summary>
    private static bool LooksLikeText(byte[] b)
    {
        int n = Math.Min(b.Length, 1024);
        if (n == 0) return true;
        int printable = 0;
        for (int i = 0; i < n; i++)
        {
            byte c = b[i];
            if (c == 0) return false;
            if (c == 9 || c == 10 || c == 13 || c >= 32) printable++;
        }
        return printable >= n * 0.90;
    }
}
