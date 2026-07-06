using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Text;
using SiegeFX.Core.Assets;
using SiegeSmith.Mvvm;
using SiegeSmith.Services;

namespace SiegeSmith.ViewModels.Viewers;

/// <summary>Structured viewer for GAS documents: parses the file into its block/attribute tree
/// and also keeps the raw text for a toggle. The constructor throws on a parse error so the
/// <see cref="ViewerFactory"/> can fall back to the plain-text reader.</summary>
public sealed class GasViewerViewModel : ObservableObject
{
    public string Name { get; }
    public string Info { get; }
    public ObservableCollection<GasTreeNode> Roots { get; } = new();
    public string RawText { get; }

    private bool _showRaw;
    public bool ShowRaw
    {
        get => _showRaw;
        set { if (SetProperty(ref _showRaw, value)) { OnPropertyChanged(nameof(ShowTree)); OnPropertyChanged(nameof(ToggleLabel)); } }
    }
    public bool ShowTree => !_showRaw;
    public string ToggleLabel => _showRaw ? "Show tree" : "Show raw";
    public RelayCommand ToggleViewCommand { get; }

    public GasViewerViewModel(string name, byte[] bytes)
    {
        Name = name;
        RawText = new UTF8Encoding(false, false).GetString(bytes);

        var doc = GasDocument.Load(bytes); // throws on parse error
        foreach (var n in doc.Roots)
        {
            var node = Build(n);
            node.IsExpanded = true; // expand the top level for orientation
            Roots.Add(node);
        }

        int blocks = 0, attrs = 0;
        Count(Roots, ref blocks, ref attrs);
        Info = $"{Format.Bytes(bytes.Length)}  ·  {blocks} block(s), {attrs} attribute(s)";
        ToggleViewCommand = new RelayCommand(_ => ShowRaw = !ShowRaw);
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
