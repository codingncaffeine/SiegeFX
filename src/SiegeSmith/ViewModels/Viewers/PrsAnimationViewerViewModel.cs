using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using SiegeFX.Core.Assets;
using SiegeSmith.Mvvm;
using SiegeSmith.Services;

namespace SiegeSmith.ViewModels.Viewers;

/// <summary>Inspector for a .prs skeletal animation clip. Surfaces the header (version, length,
/// bone count, tracers), per-bone rotation/position key counts, the note-event timeline, and any
/// embedded info strings. ED-15 — when the paired .asp rig resolves (derived from the DS1 naming
/// convention: a_* clip → m_* mesh, chore suffixes stripped), the clip PLAYS: CPU-skinned via the
/// engine's own AnimationRuntime and rasterized by the SoftwareRenderer, with play/pause and a
/// scrub slider. Disposable — the playback timer stops when the preview is swapped out.</summary>
public sealed class PrsAnimationViewerViewModel : ObservableObject, IDisposable
{
    public string Name { get; }
    public string Info { get; }
    public IReadOnlyList<InfoSection> Sections { get; }

    private const int MaxLines = 400;

    // ED-15 — live playback state.
    private readonly PrsAnimation _anim;
    private readonly AspMesh? _rig;
    private readonly Vector3 _rigCenter;
    private readonly float _rigRadius;
    private readonly DispatcherTimer? _timer;
    private float _time;
    private bool _playing;

    public bool HasRig => _rig is not null;
    public string RigInfo { get; } = "";

    private ImageSource? _frame;
    public ImageSource? Frame { get => _frame; private set => SetProperty(ref _frame, value); }

    public string PlayLabel => _playing ? "❚❚ Pause" : "▶ Play";
    public RelayCommand PlayCommand { get; }

    /// <summary>Scrub position 0..1 across the clip. Dragging pauses nothing —
    /// it just moves the clock; the next tick keeps rolling from there.</summary>
    public double ScrubPos
    {
        get => _anim.AnimLength > 0 ? _time / _anim.AnimLength : 0;
        set
        {
            _time = (float)(Math.Clamp(value, 0, 1) * _anim.AnimLength);
            RenderFrame();
            OnPropertyChanged(nameof(TimeText));
        }
    }

    public string TimeText => $"{_time:0.00} / {_anim.AnimLength:0.00} s";

    public PrsAnimationViewerViewModel(string name, PrsAnimation a, Func<string, AspMesh?>? rigResolver = null)
    {
        Name = name;
        _anim = a;
        Info = $"v{a.AnimVersion} · {a.AnimLength:F2}s · {a.NumBones} bone(s) · {a.Notes.Count} note(s)";

        // ED-15 — resolve the paired rig: a_<rig>_<chore...>.prs → m_<rig>.asp,
        // stripping trailing tokens until a skinned mesh answers.
        if (rigResolver is not null)
        {
            foreach (var candidate in RigCandidates(name))
            {
                var mesh = rigResolver(candidate);
                if (mesh is { HasSkin: true, BoneCount: > 0 })
                {
                    _rig = mesh;
                    RigInfo = $"rig: {candidate}.asp · {mesh.BoneCount} bone(s)";
                    break;
                }
            }
        }

        if (_rig is not null)
        {
            // Camera framing from the bind pose, so playback never "breathes".
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            foreach (var p in _rig.Positions) { min = Vector3.Min(min, p); max = Vector3.Max(max, p); }
            _rigCenter = (min + max) * 0.5f;
            _rigRadius = MathF.Max((max - min).Length() * 0.5f, 0.001f);

            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _timer.Tick += (_, _) =>
            {
                _time += 0.05f;
                if (_anim.AnimLength > 0 && _time > _anim.AnimLength) _time -= _anim.AnimLength;
                RenderFrame();
                OnPropertyChanged(nameof(ScrubPos));
                OnPropertyChanged(nameof(TimeText));
            };
            RenderFrame();
        }
        else if (rigResolver is not null)
        {
            RigInfo = "rig: no matching skinned .asp in this tank — info only";
        }

        PlayCommand = new RelayCommand(_ => TogglePlay(), _ => _rig is not null);

        var sections = new List<InfoSection>
        {
            new("Animation", new[]
            {
                $"version        {a.AnimVersion}",
                $"length         {a.AnimLength:F3} s",
                $"bones          {a.NumBones}",
                $"tracers        {a.TracerCount}",
                $"note events    {a.Notes.Count}",
                $"info strings   {a.InfoStrings.Count}",
            }),
        };

        var boneLines = new List<string>();
        for (int i = 0; i < a.BoneKeys.Count; i++)
        {
            var kl = a.BoneKeys[i];
            var bn = i < a.BoneNames.Count ? a.BoneNames[i] : $"bone{i}";
            int rot = kl?.RotKeys.Count ?? 0;
            int pos = kl?.PosKeys.Count ?? 0;
            boneLines.Add($"[{i,2}] {bn,-24}  rot {rot,4}   pos {pos,4}");
        }
        if (boneLines.Count > 0)
            sections.Add(new("Bone tracks (rotation / position keys)", Cap(boneLines)));

        if (a.Notes.Count > 0)
            sections.Add(new("Note events (time · token)", Cap(a.Notes.Select(n => $"{n.Time,8:F3}   0x{n.Token:X8}"))));

        if (a.InfoStrings.Count > 0)
            sections.Add(new("Info strings", Cap(a.InfoStrings)));

        Sections = sections;
    }

    /// <summary>DS1 pairs a_* clips with m_* meshes; the clip name carries
    /// chore/stance suffixes the mesh name doesn't. Yield the full swap first,
    /// then progressively strip trailing "_token"s.</summary>
    public static IEnumerable<string> RigCandidates(string prsName)
    {
        var b = Path.GetFileNameWithoutExtension(prsName);
        if (b.StartsWith("a_", StringComparison.OrdinalIgnoreCase)) b = "m_" + b[2..];
        for (int guard = 0; guard < 12; guard++)
        {
            yield return b;
            int us = b.LastIndexOf('_');
            if (us <= 2) yield break;
            b = b[..us];
        }
    }

    private void TogglePlay()
    {
        if (_timer is null) return;
        _playing = !_playing;
        if (_playing) _timer.Start(); else _timer.Stop();
        OnPropertyChanged(nameof(PlayLabel));
    }

    private void RenderFrame()
    {
        if (_rig is null) return;
        try
        {
            var skin = AnimationRuntime.ComputeSkinMatrices(_rig, _anim, _time);
            var posed = AnimationRuntime.SkinCorners(_rig, skin);
            int mtc = _rig.TriangleIndices.Length / 3;
            var verts = new Vector3[mtc * 3];
            var norms = new Vector3[mtc * 3];
            for (int t = 0; t < mtc; t++)
                for (int e = 0; e < 3; e++)
                {
                    int ci = _rig.TriangleIndices[t * 3 + e];
                    verts[t * 3 + e] = posed[ci];
                    norms[t * 3 + e] = _rig.Corners[ci].Normal; // bind normals — fine for a preview
                }
            var bgra = SoftwareRenderer.Render(verts, norms, 460, 340,
                _rigCenter, _rigRadius, yaw: -2.3f, pitch: 0.25f, dist: _rigRadius * 2.4f, wireframe: false);
            var bmp = BitmapSource.Create(460, 340, 96, 96, PixelFormats.Bgra32, null, bgra, 460 * 4);
            bmp.Freeze();
            Frame = bmp;
        }
        catch
        {
            // A malformed clip/rig pair degrades to the info readout — never a crash.
            Frame = null;
        }
    }

    private static IReadOnlyList<string> Cap(IEnumerable<string> lines)
    {
        var list = lines.Take(MaxLines + 1).ToList();
        if (list.Count > MaxLines) list[MaxLines] = $"… (truncated at {MaxLines})";
        return list;
    }

    public void Dispose() => _timer?.Stop();
}
