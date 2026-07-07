using System.Collections.Generic;
using System.Linq;
using SiegeFX.Core.Assets;
using SiegeSmith.Mvvm;

namespace SiegeSmith.ViewModels.Viewers;

/// <summary>Inspector for a .prs skeletal animation clip. Surfaces the header (version, length,
/// bone count, tracers), per-bone rotation/position key counts, the note-event timeline, and any
/// embedded info strings — reusing the <see cref="InfoSection"/> read-out style. Live skeletal
/// playback on a resolved .asp rig is a later step.</summary>
public sealed class PrsAnimationViewerViewModel : ObservableObject
{
    public string Name { get; }
    public string Info { get; }
    public IReadOnlyList<InfoSection> Sections { get; }

    private const int MaxLines = 400;

    public PrsAnimationViewerViewModel(string name, PrsAnimation a)
    {
        Name = name;
        Info = $"v{a.AnimVersion} · {a.AnimLength:F2}s · {a.NumBones} bone(s) · {a.Notes.Count} note(s)";

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

    private static IReadOnlyList<string> Cap(IEnumerable<string> lines)
    {
        var list = lines.Take(MaxLines + 1).ToList();
        if (list.Count > MaxLines) list[MaxLines] = $"… (truncated at {MaxLines})";
        return list;
    }
}
