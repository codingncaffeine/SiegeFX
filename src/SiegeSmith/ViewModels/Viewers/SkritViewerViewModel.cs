using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using SiegeSmith.Mvvm;
using SiegeSmith.Services;

namespace SiegeSmith.ViewModels.Viewers;

public enum SkritViewMode { Source, Disassembly }

/// <summary>Editor for Skrit (.skrit) scripts. Compiles live through the engine front-end on
/// every edit, surfacing a valid/error status, parser/binder diagnostics, the host-extern
/// catalogue, and the bytecode disassembly. Saves the edited source to a file.</summary>
public sealed class SkritViewerViewModel : ObservableObject
{
    public string Name { get; }
    public string Info { get; }

    private string _source;
    public string Source { get => _source; set { if (SetProperty(ref _source, value)) Recompile(); } }

    private SkritViewMode _mode = SkritViewMode.Source;
    public SkritViewMode Mode
    {
        get => _mode;
        set { if (SetProperty(ref _mode, value)) { OnPropertyChanged(nameof(ShowSource)); OnPropertyChanged(nameof(ShowDisassembly)); } }
    }
    public bool ShowSource => _mode == SkritViewMode.Source;
    public bool ShowDisassembly => _mode == SkritViewMode.Disassembly;

    private string _status = "";
    public string Status { get => _status; private set => SetProperty(ref _status, value); }

    private bool _ok = true;
    public bool Ok { get => _ok; private set => SetProperty(ref _ok, value); }

    public ObservableCollection<string> Diagnostics { get; } = new();
    public ObservableCollection<string> Externs { get; } = new();

    private string _disassembly = "";
    public string Disassembly { get => _disassembly; private set => SetProperty(ref _disassembly, value); }

    public RelayCommand ShowSourceCommand { get; }
    public RelayCommand ShowDisassemblyCommand { get; }
    public RelayCommand SaveAsCommand { get; }

    public SkritViewerViewModel(string name, byte[] bytes)
    {
        Name = name;
        _source = new UTF8Encoding(false, false).GetString(bytes);
        Info = Format.Bytes(bytes.Length);
        ShowSourceCommand = new RelayCommand(_ => Mode = SkritViewMode.Source);
        ShowDisassemblyCommand = new RelayCommand(_ => Mode = SkritViewMode.Disassembly);
        SaveAsCommand = new RelayCommand(_ => SaveAs());
        Recompile();
    }

    private void Recompile()
    {
        var r = SkritCompilerService.Compile(_source);
        Ok = r.Ok;
        Status = r.Status;
        Diagnostics.Clear();
        foreach (var d in r.Diagnostics) Diagnostics.Add(d);
        Externs.Clear();
        foreach (var e in r.Externs) Externs.Add(e);
        Disassembly = r.Disassembly;
    }

    private void SaveAs()
    {
        var dest = DialogService.SaveFileAs(Name);
        if (dest is null) return;
        try { File.WriteAllText(dest, _source); Status = $"Saved to {dest}"; }
        catch (Exception ex) { Status = "Save failed: " + ex.Message; }
    }
}
