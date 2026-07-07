using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using SiegeFX.Core.Assets;
using SiegeSmith.Mvvm;
using SiegeSmith.Services;

namespace SiegeSmith.ViewModels.Viewers;

public enum GasViewMode { Tree, Raw, Edit }

/// <summary>Viewer/editor for GAS documents. Three modes: a structured block/attribute tree, the
/// raw text, and an editable pane with live validation (re-parsed through the engine on every
/// keystroke) and save-to-file. The constructor throws on a parse error so the
/// <see cref="ViewerFactory"/> can fall back to the plain-text reader with the error shown.</summary>
public sealed class GasViewerViewModel : ObservableObject
{
    public string Name { get; }
    public string Info { get; }
    public ObservableCollection<GasTreeNode> Roots { get; } = new();
    public string RawText { get; }

    private GasViewMode _mode = GasViewMode.Tree;
    public GasViewMode Mode
    {
        get => _mode;
        set { if (SetProperty(ref _mode, value)) { OnPropertyChanged(nameof(ShowTree)); OnPropertyChanged(nameof(ShowRaw)); OnPropertyChanged(nameof(ShowEdit)); } }
    }
    public bool ShowTree => _mode == GasViewMode.Tree;
    public bool ShowRaw => _mode == GasViewMode.Raw;
    public bool ShowEdit => _mode == GasViewMode.Edit;

    private string _editText;
    public string EditText
    {
        get => _editText;
        set { if (SetProperty(ref _editText, value)) ValidateEdit(); }
    }

    private string _editStatus = "";
    public string EditStatus { get => _editStatus; private set => SetProperty(ref _editStatus, value); }

    private bool _editValid = true;
    public bool EditValid { get => _editValid; private set => SetProperty(ref _editValid, value); }

    public RelayCommand ShowTreeCommand { get; }
    public RelayCommand ShowRawCommand { get; }
    public RelayCommand EditCommand { get; }
    public RelayCommand SaveAsCommand { get; }

    public GasViewerViewModel(string name, byte[] bytes)
    {
        Name = name;
        RawText = new UTF8Encoding(false, false).GetString(bytes);
        _editText = RawText;

        var doc = GasDocument.Load(bytes); // throws on parse error
        foreach (var n in doc.Roots)
        {
            var node = Build(n);
            node.IsExpanded = true;
            Roots.Add(node);
        }
        int blocks = 0, attrs = 0;
        Count(Roots, ref blocks, ref attrs);
        Info = $"{Format.Bytes(bytes.Length)}  ·  {blocks} block(s), {attrs} attribute(s)";

        ShowTreeCommand = new RelayCommand(_ => Mode = GasViewMode.Tree);
        ShowRawCommand = new RelayCommand(_ => Mode = GasViewMode.Raw);
        EditCommand = new RelayCommand(_ => { EditText = RawText; Mode = GasViewMode.Edit; });
        SaveAsCommand = new RelayCommand(_ => SaveAs());
        ValidateEdit();
    }

    private void ValidateEdit()
    {
        var (ok, msg) = GasValidator.Validate(_editText);
        EditValid = ok;
        EditStatus = ok ? msg : "Error: " + msg;
    }

    private void SaveAs()
    {
        var (ok, msg) = GasValidator.Validate(_editText);
        if (!ok)
        {
            EditValid = false;
            EditStatus = "Not saved — fix the error first: " + msg;
            return;
        }
        var dest = DialogService.SaveFileAs(Name);
        if (dest is null) return;
        try
        {
            File.WriteAllText(dest, _editText);
            EditStatus = $"Saved to {dest}";
        }
        catch (Exception ex)
        {
            EditStatus = "Save failed: " + ex.Message;
        }
    }

    private static GasTreeNode Build(GasNode node)
    {
        var t = new GasTreeNode($"[{node.Header}]", isBlock: true);
        foreach (var a in node.Attributes)
        {
            var label = a.TypeTag is null ? $"{a.Name} = {a.Value}" : $"{a.TypeTag} {a.Name} = {a.Value}";
            t.Children.Add(new GasTreeNode(label, isBlock: false));
        }
        foreach (var c in node.Children)
            t.Children.Add(Build(c));
        return t;
    }

    private static void Count(IEnumerable<GasTreeNode> nodes, ref int blocks, ref int attrs)
    {
        foreach (var n in nodes)
        {
            if (n.IsBlock) blocks++; else attrs++;
            Count(n.Children, ref blocks, ref attrs);
        }
    }
}

/// <summary>A node in the GAS structure tree — a block header (<see cref="IsBlock"/> true, with
/// child blocks and attribute leaves) or an attribute line.</summary>
public sealed class GasTreeNode : ObservableObject
{
    public string Text { get; }
    public bool IsBlock { get; }
    public List<GasTreeNode> Children { get; } = new();

    private bool _isExpanded;
    public bool IsExpanded { get => _isExpanded; set => SetProperty(ref _isExpanded, value); }

    public GasTreeNode(string text, bool isBlock)
    {
        Text = text;
        IsBlock = isBlock;
    }
}
